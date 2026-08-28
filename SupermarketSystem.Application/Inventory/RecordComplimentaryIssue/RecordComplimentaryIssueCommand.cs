using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Inventory;

namespace SupermarketSystem.Application.Inventory.RecordComplimentaryIssue;

public static class ComplimentarySettingsKeys
{
    /// <summary>
    /// حد الكمية اليومي (لكل منتج، مجموع كل الفروع) قبل ما تُعلَّم عملية
    /// ضيافة تلقائيًا للمراجعة الإدارية. "سماح مع مراجعة" — نفس فلسفة
    /// AllowWithReview بكل النظام: العملية بتنجح دائمًا، بس تجاوز الحد
    /// بيعلّمها كـNeedsReview بدل ما يوقفها.
    /// </summary>
    public const string DailyReviewThresholdQuantity = "Complimentary.DailyReviewThresholdQuantity";
}

public sealed record RecordComplimentaryIssueCommand(
    Guid ProductId,
    Guid ProductUnitId,
    Guid BranchId,
    decimal Quantity,
    string? Reason);

public sealed record RecordComplimentaryIssueResponse(Guid StockMovementId, decimal QuantityBase, bool FlaggedForReview);

/// <summary>
/// "ضيافة" — بضاعة خرجت من المخزون بلا أي قيد مالي كإيراد. لا كيان
/// منفصل — StockMovement نفسه هو السجل الكامل.
///
/// "سماح مع مراجعة": بنجمع كل عمليات الضيافة لنفس المنتج (بكل الفروع)
/// خلال آخر 24 ساعة + العملية الحالية؛ لو المجموع تجاوز
/// DailyReviewThresholdQuantity، العملية تنجح زي العادة بس تُعلَّم
/// NeedsReview=true. لا رفض، لا تعطيل — بس إشارة للإدارة.
/// </summary>
public sealed class RecordComplimentaryIssueHandler
{
    private readonly IApplicationDbContext _context;
    private readonly IStockOperations _stockOperations;
    private readonly ITransactionalExecutor _transactionalExecutor;
    private readonly ICurrentUserContext _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ISettingsProvider _settingsProvider;

    public RecordComplimentaryIssueHandler(
        IApplicationDbContext context,
        IStockOperations stockOperations,
        ITransactionalExecutor transactionalExecutor,
        ICurrentUserContext currentUser,
        IDateTimeProvider dateTimeProvider,
        ISettingsProvider settingsProvider)
    {
        _context = context;
        _stockOperations = stockOperations;
        _transactionalExecutor = transactionalExecutor;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
        _settingsProvider = settingsProvider;
    }

    public async Task<Result<RecordComplimentaryIssueResponse>> HandleAsync(
        RecordComplimentaryIssueCommand command, CancellationToken cancellationToken)
    {
        if (command.Quantity <= 0)
        {
            return Result.Failure<RecordComplimentaryIssueResponse>(
                Error.Validation("Complimentary.QuantityMustBePositive", "الكمية يجب أن تكون موجبة."));
        }

        var productUnit = await _context.ProductUnits
            .FirstOrDefaultAsync(u => u.Id == command.ProductUnitId && u.ProductId == command.ProductId, cancellationToken);

        if (productUnit is null)
        {
            return Result.Failure<RecordComplimentaryIssueResponse>(
                Error.NotFound("Complimentary.UnitNotFound", "وحدة المنتج المحددة غير موجودة."));
        }

        var isAllowed = await _context.Products.AsNoTracking()
            .Where(p => p.Id == command.ProductId)
            .Select(p => p.IsComplimentaryAllowed)
            .FirstOrDefaultAsync(cancellationToken);

        if (!isAllowed)
        {
            return Result.Failure<RecordComplimentaryIssueResponse>(
                Error.BusinessRule("Complimentary.NotAllowedForProduct", "هذا المنتج غير مُفعَّل للضيافة — فعّله من الكتالوج أولًا."));
        }

        var userId = _currentUser.UserId
            ?? throw new InvalidOperationException("لا يمكن تسجيل ضيافة بلا هوية مستخدم مصادَق عليها.");

        var quantityBase = command.Quantity * productUnit.ConversionFactorToBase;
        var occurredAtUtc = _dateTimeProvider.UtcNow;

        var threshold = await _settingsProvider.GetDecimalAsync(
            ComplimentarySettingsKeys.DailyReviewThresholdQuantity, defaultValue: 10m, cancellationToken);

        var since = occurredAtUtc.AddHours(-24);
        var recentQuantity = await _context.StockMovements.AsNoTracking()
            .Where(m => m.ProductId == command.ProductId
                && m.MovementType == MovementType.ComplimentaryOut
                && m.OccurredAtUtc >= since)
            .SumAsync(m => m.QuantityBase, cancellationToken);

        var needsReview = (recentQuantity + quantityBase) > threshold;

        return await _transactionalExecutor.ExecuteAsync<RecordComplimentaryIssueResponse>(async ct =>
        {
            var outcome = await _stockOperations.TryDecreaseAsync(
                command.ProductId, command.BranchId, productBatchId: null, quantityBase,
                allowNegative: true, ct);

            if (outcome == StockDecrementOutcome.Failed)
            {
                return Result.Failure<RecordComplimentaryIssueResponse>(
                    Error.BusinessRule("Complimentary.InsufficientStock", "المخزون غير كافٍ لهذا المنتج بهذا الفرع."));
            }

            var movement = new StockMovement(
                command.ProductId,
                command.BranchId,
                command.ProductUnitId,
                productBatchId: null,
                quantityBase,
                MovementType.ComplimentaryOut,
                reason: command.Reason,
                occurredAtUtc,
                userId,
                StockMovementReferenceType.ManualAdjustment,
                referenceId: Guid.NewGuid(),
                needsReview: needsReview);

            _context.StockMovements.Add(movement);
            await _context.SaveChangesAsync(ct);

            return Result.Success(new RecordComplimentaryIssueResponse(movement.Id, quantityBase, needsReview));
        }, cancellationToken);
    }
}
