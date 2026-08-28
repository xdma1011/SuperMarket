using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Policies;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.CashManagement;
using SupermarketSystem.Domain.Common;
using SupermarketSystem.Domain.Identity;
using SupermarketSystem.Domain.Inventory;
using SupermarketSystem.Domain.Payments;
using SupermarketSystem.Domain.Sales;

namespace SupermarketSystem.Application.Sales.ProcessReturn;

public sealed record ProcessReturnItemDto(Guid SaleInvoiceItemId, decimal Quantity);

public sealed record ProcessReturnPaymentDto(Guid PaymentMethodId, decimal Amount, string? ExternalReference, Guid ClientRequestId);

public sealed record ProcessReturnCommand(
    Guid OriginalSaleInvoiceId,
    Guid ClientRequestId,
    ReturnReason Reason,
    string? Notes,
    IReadOnlyList<ProcessReturnItemDto> Items,
    IReadOnlyList<ProcessReturnPaymentDto> Refunds);

public sealed record ProcessReturnResponse(
    Guid ReturnInvoiceId,
    string InvoiceNumber,
    decimal TotalAmount,
    decimal TotalRefundedAmount,
    SaleInvoiceStatus OriginalInvoiceNewStatus,
    bool WasReplay,
    IReadOnlyList<string> ReviewFlags);

public static class ProcessReturnValidator
{
    public static Error? Validate(ProcessReturnCommand command)
    {
        if (command.OriginalSaleInvoiceId == Guid.Empty)
        {
            return Error.Validation("Return.OriginalInvoiceRequired", "الفاتورة الأصلية مطلوبة.");
        }

        if (command.ClientRequestId == Guid.Empty)
        {
            return Error.Validation("Return.ClientRequestIdRequired", "مفتاح الطلب (idempotency) مطلوب.");
        }

        if (command.Items.Count == 0)
        {
            return Error.Validation("Return.ItemsRequired", "يجب تحديد صنف واحد على الأقل للإرجاع.");
        }

        foreach (var item in command.Items)
        {
            if (item.SaleInvoiceItemId == Guid.Empty)
            {
                return Error.Validation("Return.ItemRequired", "كل سطر إرجاع يحتاج سطر بيع أصلي.");
            }

            if (item.Quantity <= 0)
            {
                return Error.Validation("Return.QuantityInvalid", "كمية الإرجاع لازم تكون موجبة.");
            }
        }

        if (command.Items.Select(i => i.SaleInvoiceItemId).Distinct().Count() != command.Items.Count)
        {
            return Error.Validation("Return.DuplicateItem", "لا يمكن تكرار نفس سطر البيع أكثر من مرة بنفس الإرجاع.");
        }

        foreach (var refund in command.Refunds)
        {
            if (refund.PaymentMethodId == Guid.Empty)
            {
                return Error.Validation("Return.RefundMethodRequired", "كل استرجاع يحتاج طريقة دفع.");
            }

            if (refund.Amount <= 0)
            {
                return Error.Validation("Return.RefundAmountInvalid", "مبلغ الاسترجاع لازم يكون موجبًا.");
            }

            if (refund.ClientRequestId == Guid.Empty)
            {
                return Error.Validation("Return.RefundClientRequestIdRequired", "كل استرجاع يحتاج مفتاح طلب.");
            }
        }

        if (command.Refunds.Select(r => r.ClientRequestId).Distinct().Count() != command.Refunds.Count)
        {
            return Error.Validation("Return.DuplicateRefundRequestId", "مفاتيح طلبات الاسترجاع لازم تكون فريدة.");
        }

        return null;
    }
}

/// <summary>
/// معالجة إرجاع من زبون — أعقد عملية بالنظام بعد البيع نفسه.
///
/// ═══════════════════════════════════════════════════════════════════
/// ملاحظتان أساسيتان على التصميم:
/// ═══════════════════════════════════════════════════════════════════
///
/// 1) الكميات محروسة ذريًا (ISaleInvoiceOperations)، لا بالذاكرة.
///    إرجاعان متزامنان لنفس السطر لا يقدروا الاثنين ينجحوا. الفحص
///    بالذاكرة (SaleInvoiceItem.RecordReturn) موجود كطبقة ثانية، لكنه
///    ليس الحارس الفعلي هون.
///
/// 2) السعر يُؤخذ من *لقطة البيع الأصلية* (UnitPriceSnapshot)، لا من
///    سعر المنتج الحالي. لو تغيّر سعر المنتج بعد البيعة، الزبون يسترجع
///    بالضبط ما دفعه، لا أكثر ولا أقل — وهذا هو المقصود بـ"الفاتورة
///    المكتملة حقيقة تاريخية".
///
/// الإرجاع لا ينتظر موافقة أحد: الفحوصات السياسية كلها فورية من إعدادات
/// مخزّنة، والرفض جواب نهائي لحظي. الإرجاع بقيمة عالية أو باسترجاع
/// بطريقة مختلفة عن الأصل *يُنفَّذ ويُعلَّم للمراجعة*، لا يُمنع.
/// </summary>
public sealed class ProcessReturnHandler
{
    private readonly IApplicationDbContext _context;
    private readonly IDocumentNumberGenerator _documentNumberGenerator;
    private readonly ISaleInvoiceOperations _saleInvoiceOperations;
    private readonly ITransactionalExecutor _transactionalExecutor;
    private readonly IPosPolicyService _posPolicyService;
    private readonly INotificationDispatcher _notificationDispatcher;
    private readonly ICurrentUserContext _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;

    public ProcessReturnHandler(
        IApplicationDbContext context,
        IDocumentNumberGenerator documentNumberGenerator,
        ISaleInvoiceOperations saleInvoiceOperations,
        ITransactionalExecutor transactionalExecutor,
        IPosPolicyService posPolicyService,
        INotificationDispatcher notificationDispatcher,
        ICurrentUserContext currentUser,
        IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _documentNumberGenerator = documentNumberGenerator;
        _saleInvoiceOperations = saleInvoiceOperations;
        _transactionalExecutor = transactionalExecutor;
        _posPolicyService = posPolicyService;
        _notificationDispatcher = notificationDispatcher;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<ProcessReturnResponse>> HandleAsync(ProcessReturnCommand command, CancellationToken cancellationToken)
    {
        var validationError = ProcessReturnValidator.Validate(command);
        if (validationError is not null)
        {
            return Result.Failure<ProcessReturnResponse>(validationError);
        }

        // === 1. Idempotency: هل هذا الإرجاع بالذات مسجَّل أصلًا؟ ===
        var replay = await _context.ReturnInvoices.AsNoTracking()
            .Where(r => r.ClientRequestId == command.ClientRequestId)
            .Select(r => new { r.Id, r.InvoiceNumber, r.TotalAmount, r.TotalRefundedAmount, r.OriginalSaleInvoiceId })
            .FirstOrDefaultAsync(cancellationToken);

        if (replay is not null)
        {
            var originalStatus = await _context.SaleInvoices.AsNoTracking()
                .Where(s => s.Id == replay.OriginalSaleInvoiceId)
                .Select(s => s.Status)
                .FirstAsync(cancellationToken);

            return Result.Success(new ProcessReturnResponse(
                replay.Id, replay.InvoiceNumber, replay.TotalAmount, replay.TotalRefundedAmount,
                originalStatus, WasReplay: true, Array.Empty<string>()));
        }

        // === 2. الفاتورة الأصلية وأسطرها ===
        var originalInvoice = await _context.SaleInvoices
            .Include(s => s.Items)
            .Include(s => s.Payments)
            .FirstOrDefaultAsync(s => s.Id == command.OriginalSaleInvoiceId, cancellationToken);

        if (originalInvoice is null)
        {
            return Result.Failure<ProcessReturnResponse>(
                Error.NotFound("Return.OriginalInvoiceNotFound", $"الفاتورة '{command.OriginalSaleInvoiceId}' غير موجودة."));
        }

        if (originalInvoice.Status is SaleInvoiceStatus.Voided or SaleInvoiceStatus.FullyReturned)
        {
            return Result.Failure<ProcessReturnResponse>(Error.BusinessRule(
                "Return.InvoiceNotReturnable", $"لا يمكن الإرجاع على فاتورة بحالة {originalInvoice.Status}."));
        }

        var saleItemsById = originalInvoice.Items.ToDictionary(i => i.Id);

        foreach (var requested in command.Items)
        {
            if (!saleItemsById.TryGetValue(requested.SaleInvoiceItemId, out var saleItem))
            {
                return Result.Failure<ProcessReturnResponse>(Error.Validation(
                    "Return.ItemNotInInvoice", $"السطر '{requested.SaleInvoiceItemId}' لا ينتمي لهذه الفاتورة."));
            }

            // فحص مبكر ودّي — الحارس الفعلي هو الجملة الذرية جوّا المعاملة.
            if (saleItem.QuantityReturned + requested.Quantity > saleItem.Quantity)
            {
                return Result.Failure<ProcessReturnResponse>(Error.BusinessRule(
                    "Return.ExceedsSoldQuantity",
                    $"كمية الإرجاع تتجاوز المتبقي من الكمية المباعة للسطر '{requested.SaleInvoiceItemId}'."));
            }
        }

        // === 3. الحساب من لقطة البيع الأصلية، لا من الأسعار الحالية ===
        var returnTotal = command.Items.Sum(i => i.Quantity * saleItemsById[i.SaleInvoiceItemId].UnitPriceSnapshot);
        var refundsTotal = command.Refunds.Sum(r => r.Amount);

        if (refundsTotal > returnTotal)
        {
            return Result.Failure<ProcessReturnResponse>(Error.BusinessRule(
                "Return.RefundExceedsReturnTotal",
                $"مجموع الاسترجاع ({refundsTotal}) يتجاوز قيمة الإرجاع ({returnTotal})."));
        }

        var reviewFlags = new List<string>();

        // === 4. فحوصات السياسة — كلها فورية، بلا انتظار ===
        var returnDecision = await _posPolicyService.EvaluateAsync(
            PosOperation.ProcessReturn, returnTotal, comparisonBase: null, cancellationToken);

        if (!returnDecision.IsAllowed)
        {
            return Result.Failure<ProcessReturnResponse>(
                Error.Forbidden("Return.Denied", returnDecision.Reason ?? "الإرجاع غير مسموح حاليًا."));
        }

        if (returnDecision.RequiresReview && returnDecision.Reason is not null)
        {
            reviewFlags.Add(returnDecision.Reason);
        }

        // الاسترجاع بطريقة غير طريقة الدفع الأصلية — الحالة الوحيدة اللي
        // بتخلق فجوة حقيقية بين الدرج والبنك (§16.10). مسموحة أو ممنوعة
        // حسب الإعداد، وبكل الأحوال تُعلَّم لو انسمحت.
        var originalMethodIds = originalInvoice.Payments
            .Where(p => p.Status == PaymentStatus.Completed)
            .Select(p => p.PaymentMethodId)
            .Distinct()
            .ToHashSet();

        var refundMethodIds = command.Refunds.Select(r => r.PaymentMethodId).Distinct().ToList();
        var hasCrossMethodRefund = refundMethodIds.Any(id => !originalMethodIds.Contains(id));

        if (hasCrossMethodRefund)
        {
            var crossDecision = await _posPolicyService.EvaluateAsync(PosOperation.CrossMethodRefund, cancellationToken);

            if (!crossDecision.IsAllowed)
            {
                return Result.Failure<ProcessReturnResponse>(
                    Error.Forbidden("Return.CrossMethodRefundDenied", crossDecision.Reason ?? "الاسترجاع بطريقة دفع مختلفة غير مسموح."));
            }

            if (crossDecision.Reason is not null)
            {
                reviewFlags.Add(crossDecision.Reason);
            }
        }

        // === 5. طرق الاسترجاع لازم تكون موجودة وفعّالة ===
        var refundMethods = await _context.PaymentMethods.AsNoTracking()
            .Where(pm => refundMethodIds.Contains(pm.Id))
            .Select(pm => new { pm.Id, pm.Name, pm.IsActive, pm.AffectsCashDrawer, pm.RequiresExternalReference })
            .ToDictionaryAsync(pm => pm.Id, cancellationToken);

        foreach (var refund in command.Refunds)
        {
            if (!refundMethods.TryGetValue(refund.PaymentMethodId, out var method))
            {
                return Result.Failure<ProcessReturnResponse>(
                    Error.NotFound("Return.PaymentMethodNotFound", $"طريقة الدفع '{refund.PaymentMethodId}' غير موجودة."));
            }

            if (!method.IsActive)
            {
                return Result.Failure<ProcessReturnResponse>(
                    Error.BusinessRule("Return.PaymentMethodInactive", $"طريقة الدفع '{method.Name}' لم تعد فعّالة."));
            }

            if (method.RequiresExternalReference && string.IsNullOrWhiteSpace(refund.ExternalReference))
            {
                return Result.Failure<ProcessReturnResponse>(Error.Validation(
                    "Return.ExternalReferenceRequired", $"طريقة الدفع '{method.Name}' تتطلب مرجعًا خارجيًا."));
            }
        }

        // === 6. الوحدات (لتحويل الكمية للوحدة الأساسية عند إرجاع المخزون) ===
        var unitIds = command.Items.Select(i => saleItemsById[i.SaleInvoiceItemId].ProductUnitId).Distinct().ToList();
        var unitFactors = await _context.ProductUnits.AsNoTracking()
            .Where(u => unitIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.ConversionFactorToBase, cancellationToken);

        // الدفعة الأصلية لكل سطر — من حركة المخزون الأصلية، مش من سطر
        // الفاتورة (SaleInvoiceItem ما بتخزّن ProductBatchId إطلاقًا).
        // نفس اكتشاف D9 بالضبط: البضاعة لازم ترجع للدفعة اللي طلعت منها.
        var saleItemIds = command.Items.Select(i => i.SaleInvoiceItemId).ToList();
        var originalMovements = await _context.StockMovements.AsNoTracking()
            .Where(m => m.ReferenceType == StockMovementReferenceType.SaleInvoiceItem
                        && saleItemIds.Contains(m.ReferenceId)
                        && m.MovementType == MovementType.SaleOut)
            .ToListAsync(cancellationToken);

        var batchBySaleItem = originalMovements
            .GroupBy(m => m.ReferenceId)
            .ToDictionary(g => g.Key, g => g.First().ProductBatchId);

        var invoiceNumber = await _documentNumberGenerator.GetNextNumberAsync(
            originalInvoice.BranchId, DocumentType.ReturnInvoice, cancellationToken);

        var actorUserId = _currentUser.UserId ?? User.SystemUserId;
        var occurredAtUtc = _dateTimeProvider.UtcNow;

        // === 7. الجزء الذري ===
        var result = await _transactionalExecutor.ExecuteAsync<ProcessReturnResponse>(async ct =>
        {
            // الحارس الذري أولًا — أكثر خطوة احتمالًا للفشل.
            foreach (var requested in command.Items)
            {
                var recorded = await _saleInvoiceOperations.TryRecordReturnedQuantityAsync(
                    requested.SaleInvoiceItemId, requested.Quantity, ct);

                if (!recorded)
                {
                    return Result.Failure<ProcessReturnResponse>(Error.BusinessRule(
                        "Return.ExceedsSoldQuantity",
                        "كمية الإرجاع تتجاوز المتبقي من الكمية المباعة (ربما سُجِّل إرجاع آخر للسطر نفسه بنفس اللحظة)."));
                }
            }

            var returnInvoice = new ReturnInvoice(
                originalInvoice.BranchId, invoiceNumber, command.ClientRequestId,
                originalInvoice.Id, command.Reason, command.Notes);

            var movements = new List<StockMovement>();

            foreach (var requested in command.Items)
            {
                var saleItem = saleItemsById[requested.SaleInvoiceItemId];

                returnInvoice.AddItem(
                    saleItem.Id, saleItem.ProductId, saleItem.ProductUnitId,
                    requested.Quantity, saleItem.UnitPriceSnapshot);

                var quantityBase = requested.Quantity * unitFactors[saleItem.ProductUnitId];
                var batchId = batchBySaleItem.GetValueOrDefault(saleItem.Id);

                var stock = await GetOrCreateTrackedStockAsync(saleItem.ProductId, originalInvoice.BranchId, batchId, ct);
                stock.Increase(quantityBase);

                movements.Add(new StockMovement(
                    saleItem.ProductId, originalInvoice.BranchId, saleItem.ProductUnitId, batchId,
                    quantityBase, MovementType.ReturnIn, $"إرجاع {invoiceNumber}",
                    occurredAtUtc, actorUserId, StockMovementReferenceType.SaleInvoiceItem, saleItem.Id));
            }

            var cashMovements = new List<CashDrawerLog>();

            foreach (var refund in command.Refunds)
            {
                var refundPayment = returnInvoice.AddPayment(
                    refund.PaymentMethodId, refund.Amount, actorUserId,
                    originalInvoice.BranchId, refund.ExternalReference, refund.ClientRequestId);

                if (refundMethods[refund.PaymentMethodId].AffectsCashDrawer)
                {
                    cashMovements.Add(new CashDrawerLog(
                        originalInvoice.BranchId,
                        CashDrawerMovementType.ReturnCashOut,
                        refund.Amount,
                        CashDrawerReferenceType.ReturnInvoicePayment,
                        refundPayment.Id,
                        actorUserId,
                        occurredAtUtc));
                }
            }

            // === تحديث حالة الفاتورة الأصلية ===
            // القيم الطازجة تُقرأ من قاعدة البيانات بعد التحديثات الذرية —
            // الكيانات المحمّلة بالذاكرة قديمة الآن (ExecuteUpdate بيتجاوز
            // متتبِّع التغييرات عمدًا)، فالاعتماد عليها هون بيعطي حالة غلط.
            var freshQuantities = await _context.SaleInvoiceItems.AsNoTracking()
                .Where(i => i.SaleInvoiceId == originalInvoice.Id)
                .Select(i => new { i.Id, i.Quantity, i.QuantityReturned })
                .ToListAsync(ct);

            var allFullyReturned = freshQuantities.All(i => i.QuantityReturned >= i.Quantity);

            originalInvoice.RegisterReturn(returnInvoice.TotalAmount, allFullyReturned);

            _context.ReturnInvoices.Add(returnInvoice);
            _context.StockMovements.AddRange(movements);
            _context.CashDrawerLogs.AddRange(cashMovements);

            await _context.SaveChangesAsync(ct);

            return Result.Success(new ProcessReturnResponse(
                returnInvoice.Id, returnInvoice.InvoiceNumber, returnInvoice.TotalAmount,
                returnInvoice.TotalRefundedAmount, originalInvoice.Status, WasReplay: false, reviewFlags));
        }, cancellationToken);

        // التنبيه بعد الالتزام فقط.
        if (result.IsSuccess && result.Value.ReviewFlags.Count > 0)
        {
            await _notificationDispatcher.NotifyAsync(
                $"مراجعة مطلوبة — إرجاع {result.Value.InvoiceNumber}",
                $"- {string.Join("\n- ", result.Value.ReviewFlags)}",
                cancellationToken);
        }

        return result;
    }

    private async Task<Stock> GetOrCreateTrackedStockAsync(
        Guid productId, Guid branchId, Guid? productBatchId, CancellationToken cancellationToken)
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
