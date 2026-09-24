using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Domain.Notifications;
using SupermarketSystem.Application.Common.Notifications;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Inventory;

namespace SupermarketSystem.Application.Inventory.RecordWasteIssue;

public static class WasteSettingsKeys
{
    /// <summary>
    /// حد الكمية اليومي (لكل منتج، مجموع كل الفروع) قبل ما تُعلَّم عملية
    /// تلف/هلاك تلقائيًا للمراجعة الإدارية - نفس فلسفة
    /// ComplimentarySettingsKeys.DailyReviewThresholdQuantity بالضبط، بس
    /// مفتاح إعداد مستقل (تلف وضيافة معدَّلان بشكل مستقل غالبًا).
    /// </summary>
    public const string DailyReviewThresholdQuantity = "Waste.DailyReviewThresholdQuantity";
}

public sealed record RecordWasteIssueCommand(
    Guid ProductId,
    Guid ProductUnitId,
    Guid BranchId,
    decimal Quantity,
    WasteReason Reason,
    string? Notes,
    bool IsReplacedBySupplier = false);

public sealed record RecordWasteIssueResponse(Guid StockMovementId, decimal QuantityBase, bool FlaggedForReview);

/// <summary>
/// "تلف/هلاك" — بضاعة خرجت من المخزون بسبب تلف فعلي، منفصلة تصنيفًا عن
/// الضيافة (RecordComplimentaryIssueHandler) رغم تشابه الآلية تمامًا -
/// راجع تعليق MovementType.WasteOut بالـDomain. بخلاف الضيافة، أي منتج
/// قابل للتلف (لا حاجة لعلم تفعيل مسبق زي IsComplimentaryAllowed).
///
/// "سماح مع مراجعة": نفس آلية الضيافة بالضبط، بس بعتبة مستقلة
/// (WasteSettingsKeys.DailyReviewThresholdQuantity) ومجمَّعة على
/// MovementType.WasteOut فقط (لا تختلط بعدّاد الضيافة).
///
/// IsReplacedBySupplier: الشركة عوّضت البضاعة - الحركة بتنقص المخزون عادي،
/// بس ما بتنحسب خسارة بكشف الربح الشهري (راجع GetMonthlyProfitStatementHandler).
/// </summary>
public sealed class RecordWasteIssueHandler
{
    private readonly IApplicationDbContext _context;
    private readonly IStockOperations _stockOperations;
    private readonly ITransactionalExecutor _transactionalExecutor;
    private readonly ICurrentUserContext _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ISettingsProvider _settingsProvider;
    private readonly INotificationDispatcher _notificationDispatcher;

    public RecordWasteIssueHandler(
        IApplicationDbContext context,
        IStockOperations stockOperations,
        ITransactionalExecutor transactionalExecutor,
        ICurrentUserContext currentUser,
        IDateTimeProvider dateTimeProvider,
        ISettingsProvider settingsProvider,
        INotificationDispatcher notificationDispatcher)
    {
        _notificationDispatcher = notificationDispatcher;
        _context = context;
        _stockOperations = stockOperations;
        _transactionalExecutor = transactionalExecutor;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
        _settingsProvider = settingsProvider;
    }

    public async Task<Result<RecordWasteIssueResponse>> HandleAsync(
        RecordWasteIssueCommand command, CancellationToken cancellationToken)
    {
        if (command.Quantity <= 0)
        {
            return Result.Failure<RecordWasteIssueResponse>(
                Error.Validation("Waste.QuantityMustBePositive", "الكمية يجب أن تكون موجبة."));
        }

        var productUnit = await _context.ProductUnits
            .FirstOrDefaultAsync(u => u.Id == command.ProductUnitId && u.ProductId == command.ProductId, cancellationToken);

        if (productUnit is null)
        {
            return Result.Failure<RecordWasteIssueResponse>(
                Error.NotFound("Waste.UnitNotFound", "وحدة المنتج المحددة غير موجودة."));
        }

        var userId = _currentUser.UserId
            ?? throw new InvalidOperationException("لا يمكن تسجيل تلف/هلاك بلا هوية مستخدم مصادَق عليها.");

        var quantityBase = command.Quantity * productUnit.ConversionFactorToBase;
        var occurredAtUtc = _dateTimeProvider.UtcNow;

        var threshold = await _settingsProvider.GetDecimalAsync(
            WasteSettingsKeys.DailyReviewThresholdQuantity, defaultValue: 10m, cancellationToken);

        var since = occurredAtUtc.AddHours(-24);
        var recentQuantity = await _context.StockMovements.AsNoTracking()
            .Where(m => m.ProductId == command.ProductId
                && m.MovementType == MovementType.WasteOut
                && m.OccurredAtUtc >= since)
            .SumAsync(m => m.QuantityBase, cancellationToken);

        var needsReview = (recentQuantity + quantityBase) > threshold;

        var result = await _transactionalExecutor.ExecuteAsync<RecordWasteIssueResponse>(async ct =>
        {
            var outcome = await _stockOperations.TryDecreaseAsync(
                command.ProductId, command.BranchId, productBatchId: null, quantityBase,
                allowNegative: true, ct);

            if (outcome == StockDecrementOutcome.Failed)
            {
                return Result.Failure<RecordWasteIssueResponse>(
                    Error.BusinessRule("Waste.InsufficientStock", "المخزون غير كافٍ لهذا المنتج بهذا الفرع."));
            }

            var movement = new StockMovement(
                command.ProductId,
                command.BranchId,
                command.ProductUnitId,
                productBatchId: null,
                quantityBase,
                MovementType.WasteOut,
                reason: command.Notes,
                occurredAtUtc,
                userId,
                StockMovementReferenceType.ManualAdjustment,
                referenceId: Guid.NewGuid(),
                needsReview: needsReview,
                wasteReason: command.Reason,
                isReplacedBySupplier: command.IsReplacedBySupplier);

            _context.StockMovements.Add(movement);
            await _context.SaveChangesAsync(ct);

            return Result.Success(new RecordWasteIssueResponse(movement.Id, quantityBase, needsReview));
        }, cancellationToken);

        // فوق الحد اليومي = بينبّه فورًا (مش بس بقائمة المراجعات)، "سماح مع مراجعة" §1.6 - العملية
        // نجحت، بس خروج بضاعة بلا بيع بكمية كبيرة هو باب سرقة لازم صاحب المحل يشوفه.
        if (result.IsSuccess && result.Value.FlaggedForReview)
        {
            var productName = await AlertText.ProductNameAsync(_context, command.ProductId, cancellationToken);
            var branchName = await AlertText.BranchNameAsync(_context, command.BranchId, cancellationToken);
            var actor = await AlertText.UserNameAsync(_context, _currentUser.UserId, cancellationToken);
            await _notificationDispatcher.NotifyAsync(
                $"تلف فوق الحد اليومي — {productName}",
                $"الفرع: {branchName}\nسجّلها: {actor}\nالكمية: {result.Value.QuantityBase:0.###} (مجموع آخر 24 ساعة تجاوز الحد {threshold:0.###})" + $"\nالسبب: {AlertText.WasteReason(command.Reason)}" +
                (string.IsNullOrWhiteSpace(command.Notes) ? "" : $"\nملاحظة: {command.Notes}"),
                cancellationToken,
                NotificationSeverity.Warning);
        }

        return result;
    }
}