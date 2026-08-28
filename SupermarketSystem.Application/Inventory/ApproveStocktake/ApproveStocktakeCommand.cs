using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Identity;
using SupermarketSystem.Domain.Inventory;

namespace SupermarketSystem.Application.Inventory.ApproveStocktake;

public sealed record ApproveStocktakeCommand(Guid StocktakeId);

public sealed record ApproveStocktakeAppliedCorrectionDto(Guid ProductId, decimal Variance, bool WentNegative);

public sealed record ApproveStocktakeResponse(
    Guid StocktakeId,
    string StocktakeNumber,
    IReadOnlyList<ApproveStocktakeAppliedCorrectionDto> AppliedCorrections);

/// <summary>
/// الخطوة الوحيدة بدورة حياة الجرد اللي فيها Stock فعليًا يتغيّر —
/// بمعاملة قاعدة بيانات واحدة، كل التصحيحات تلتزم سوا أو ولا وحدة.
///
/// الاتجاهان مختلفان تقنيًا وليس بالصدفة:
///  - زيادة (الجرد لقى أكتر من المتوقع): Stock.Increase() العادي —
///    بلا خطر "بيع مضاعف"، نفس منطق استلام الشراء بـD6.
///  - نقصان (الجرد لقى أقل من المتوقع): IStockOperations.TryDecreaseAsync
///    الذري — **نفس آلية خصم البيع بالضبط**، لأنه فعليًا نفس المخاطرة:
///    اعتماد الجرد ممكن يتزامن مع بيعة حقيقية شغّالة على نفس المنتج بنفس
///    اللحظة. استخدام أي آلية تانية (تحميل-تعديل-حفظ) كان رح يرجّع نفس
///    ثغرة التزامن اللي بنينا كل التصميم لتفاديها بـD7.
///  - يحترم إعداد AllowNegativeStock نفسه (لو التصحيح نزل الرصيد تحت
///    الصفر ومسموح، بيكمل ويُعلَّم WentNegative — لا يوقف الاعتماد).
///
/// StockMovement تتطلب ProductUnitId (وحدة الحركة) — الجرد بيعدّ بالوحدة
/// الأساسية دائمًا (Stock.QuantityOnHand نفسها بالوحدة الأساسية أصلًا، لا
/// خيار وحدة وقت العدّ)، فوحدات كل المنتجات المعنية تُجلَب دفعة وحدة قبل
/// الحلقة (لا استعلام لكل صنف داخلها).
/// </summary>
public sealed class ApproveStocktakeHandler
{
    private readonly IApplicationDbContext _context;
    private readonly IStockOperations _stockOperations;
    private readonly ITransactionalExecutor _transactionalExecutor;
    private readonly ISettingsProvider _settingsProvider;
    private readonly ICurrentUserContext _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;

    public ApproveStocktakeHandler(
        IApplicationDbContext context,
        IStockOperations stockOperations,
        ITransactionalExecutor transactionalExecutor,
        ISettingsProvider settingsProvider,
        ICurrentUserContext currentUser,
        IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _stockOperations = stockOperations;
        _transactionalExecutor = transactionalExecutor;
        _settingsProvider = settingsProvider;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<ApproveStocktakeResponse>> HandleAsync(
        ApproveStocktakeCommand command, CancellationToken cancellationToken)
    {
        var stocktake = await _context.Stocktakes
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.Id == command.StocktakeId, cancellationToken);

        if (stocktake is null)
        {
            return Result.Failure<ApproveStocktakeResponse>(
                Error.NotFound("Stocktake.NotFound", $"الجرد '{command.StocktakeId}' غير موجود."));
        }

        if (stocktake.Status != StocktakeStatus.Completed)
        {
            return Result.Failure<ApproveStocktakeResponse>(
                Error.BusinessRule("Stocktake.NotCompleted", $"لا يمكن اعتماد جرد بحالة {stocktake.Status}؛ يجب إكماله أولًا."));
        }

        var variantItems = stocktake.Items.Where(i => i.VarianceQuantity is not 0 and not null).ToList();

        if (variantItems.Count == 0)
        {
            stocktake.Approve(_currentUser.UserId ?? User.SystemUserId, _dateTimeProvider.UtcNow);
            await _context.SaveChangesAsync(cancellationToken);
            return Result.Success(new ApproveStocktakeResponse(stocktake.Id, stocktake.StocktakeNumber, Array.Empty<ApproveStocktakeAppliedCorrectionDto>()));
        }

        // وحدة أساسية لكل منتج معني — دفعة واحدة، لا استعلام داخل الحلقة.
        var productIds = variantItems.Select(i => i.ProductId).Distinct().ToList();
        var baseUnitByProduct = await _context.ProductUnits.AsNoTracking()
            .Where(u => productIds.Contains(u.ProductId) && u.IsBaseUnit)
            .ToDictionaryAsync(u => u.ProductId, u => u.Id, cancellationToken);

        var allowNegativeStock = await _settingsProvider.GetBoolAsync(
            InventorySettingsKeys.AllowNegativeStock, defaultValue: true, cancellationToken);

        var actorUserId = _currentUser.UserId ?? User.SystemUserId;
        var occurredAtUtc = _dateTimeProvider.UtcNow;

        return await _transactionalExecutor.ExecuteAsync<ApproveStocktakeResponse>(async ct =>
        {
            var appliedCorrections = new List<ApproveStocktakeAppliedCorrectionDto>();
            var movements = new List<StockMovement>();

            foreach (var item in variantItems)
            {
                if (!baseUnitByProduct.TryGetValue(item.ProductId, out var unitId))
                {
                    return Result.Failure<ApproveStocktakeResponse>(Error.BusinessRule(
                        "Stocktake.NoBaseUnit", $"المنتج '{item.ProductId}' ليس له وحدة أساسية معرَّفة."));
                }

                var variance = item.VarianceQuantity!.Value;

                if (variance > 0)
                {
                    var stock = await GetOrCreateTrackedStockAsync(stocktake.BranchId, item.ProductId, item.ProductBatchId, ct);
                    stock.Increase(variance);

                    movements.Add(new StockMovement(
                        item.ProductId, stocktake.BranchId, unitId, item.ProductBatchId,
                        variance, MovementType.StocktakeCorrectionIncrease, reason: $"جرد {stocktake.StocktakeNumber}",
                        occurredAtUtc, actorUserId, StockMovementReferenceType.StocktakeItem, item.Id));

                    appliedCorrections.Add(new ApproveStocktakeAppliedCorrectionDto(item.ProductId, variance, WentNegative: false));
                }
                else
                {
                    var decreaseAmount = Math.Abs(variance);
                    var outcome = await _stockOperations.TryDecreaseAsync(
                        item.ProductId, stocktake.BranchId, item.ProductBatchId, decreaseAmount, allowNegativeStock, ct);

                    if (outcome == StockDecrementOutcome.Failed)
                    {
                        return Result.Failure<ApproveStocktakeResponse>(Error.BusinessRule(
                            "Stocktake.NegativeStockNotAllowed",
                            $"تعذّر اعتماد تصحيح المنتج '{item.ProductId}' — المخزون السالب غير مسموح حاليًا."));
                    }

                    movements.Add(new StockMovement(
                        item.ProductId, stocktake.BranchId, unitId, item.ProductBatchId,
                        decreaseAmount, MovementType.StocktakeCorrectionDecrease, reason: $"جرد {stocktake.StocktakeNumber}",
                        occurredAtUtc, actorUserId, StockMovementReferenceType.StocktakeItem, item.Id));

                    appliedCorrections.Add(new ApproveStocktakeAppliedCorrectionDto(
                        item.ProductId, variance, WentNegative: outcome == StockDecrementOutcome.SucceededWentNegative));
                }
            }

            stocktake.Approve(actorUserId, occurredAtUtc);

            _context.StockMovements.AddRange(movements);
            await _context.SaveChangesAsync(ct);

            return Result.Success(new ApproveStocktakeResponse(stocktake.Id, stocktake.StocktakeNumber, appliedCorrections));
        }, cancellationToken);
    }

    private async Task<Stock> GetOrCreateTrackedStockAsync(Guid branchId, Guid productId, Guid? productBatchId, CancellationToken cancellationToken)
    {
        var existing = await _context.Stocks.FirstOrDefaultAsync(
            s => s.ProductId == productId && s.BranchId == branchId && s.ProductBatchId == productBatchId,
            cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var created = new Stock(productId, branchId, productBatchId);
        _context.Stocks.Add(created);
        return created;
    }
}
