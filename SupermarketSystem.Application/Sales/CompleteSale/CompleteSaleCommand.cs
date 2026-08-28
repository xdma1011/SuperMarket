using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Policies;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.CashManagement;
using SupermarketSystem.Domain.Common;
using SupermarketSystem.Domain.Identity;
using SupermarketSystem.Domain.Inventory;
using SupermarketSystem.Domain.Sales;

namespace SupermarketSystem.Application.Sales.CompleteSale;

/// <summary>
/// NOTE WHAT IS ABSENT: there is no UnitPrice field. The selling price is
/// read server-side from ProductBranch and snapshotted; a client cannot
/// propose one. Accepting a price from the POS terminal would let anyone
/// able to call this endpoint sell at any price they chose, which no
/// after-the-fact report could reliably distinguish from a legitimate sale.
///
/// ManualDiscountAmount IS accepted, because an ad-hoc checkout discount is
/// a real business need — but it is bounded by policy (§16.6 /
/// PosPolicyKeys.MaxManualDiscountPercentage) and recorded with
/// DiscountId = null, which is exactly what makes it show up in the manual
/// discounts management report.
/// </summary>
public sealed record CompleteSaleItemDto(
    Guid ProductId,
    Guid ProductUnitId,
    decimal Quantity,
    decimal ManualDiscountAmount,
    // Required when the product is batch-tracked. No batch is auto-selected — see handler remarks on costing.
    Guid? ProductBatchId);

public sealed record CompleteSalePaymentDto(
    Guid PaymentMethodId,
    decimal Amount,
    string? ExternalReference,
    Guid ClientRequestId);

public sealed record CompleteSaleCommand(
    Guid BranchId,
    // Idempotency key for the sale as a whole. A retry with the same value returns the original sale instead of creating a second one.
    Guid ClientRequestId,
    Guid? CustomerId,
    decimal InvoiceLevelDiscountAmount,
    IReadOnlyList<CompleteSaleItemDto> Items,
    IReadOnlyList<CompleteSalePaymentDto> Payments);

public sealed record CompleteSaleResponse(
    Guid SaleInvoiceId,
    string InvoiceNumber,
    decimal TotalAmount,
    decimal TotalPaidAmount,
    bool WasReplay,
    IReadOnlyList<string> ReviewFlags);

public static class CompleteSaleValidator
{
    public static Error? Validate(CompleteSaleCommand command)
    {
        if (command.BranchId == Guid.Empty)
        {
            return Error.Validation("Sale.BranchRequired", "A branch is required.");
        }

        if (command.ClientRequestId == Guid.Empty)
        {
            return Error.Validation("Sale.ClientRequestIdRequired", "A client request id is required as the sale's idempotency key.");
        }

        if (command.Items.Count == 0)
        {
            return Error.Validation("Sale.ItemsRequired", "A sale must contain at least one item.");
        }

        if (command.InvoiceLevelDiscountAmount < 0)
        {
            return Error.Validation("Sale.InvoiceDiscountNegative", "Invoice discount cannot be negative.");
        }

        foreach (var item in command.Items)
        {
            if (item.ProductId == Guid.Empty || item.ProductUnitId == Guid.Empty)
            {
                return Error.Validation("Sale.ItemProductRequired", "Every item requires a product and unit.");
            }

            if (item.Quantity <= 0)
            {
                return Error.Validation("Sale.ItemQuantityInvalid", "Every item's quantity must be positive.");
            }

            if (item.ManualDiscountAmount < 0)
            {
                return Error.Validation("Sale.ItemDiscountNegative", "Item discount cannot be negative.");
            }
        }

        foreach (var payment in command.Payments)
        {
            if (payment.PaymentMethodId == Guid.Empty)
            {
                return Error.Validation("Sale.PaymentMethodRequired", "Every payment requires a payment method.");
            }

            if (payment.Amount <= 0)
            {
                return Error.Validation("Sale.PaymentAmountInvalid", "Every payment amount must be positive.");
            }

            if (payment.ClientRequestId == Guid.Empty)
            {
                return Error.Validation("Sale.PaymentClientRequestIdRequired", "Every payment requires a client request id.");
            }
        }

        if (command.Payments.Select(p => p.ClientRequestId).Distinct().Count() != command.Payments.Count)
        {
            return Error.Validation("Sale.DuplicatePaymentRequestId", "Payment client request ids must be unique within a sale.");
        }

        return null;
    }
}

/// <summary>
/// The central POS transaction. Everything built in Phases A–C converges
/// here, so the ordering below is deliberate rather than incidental:
///
///  1. Idempotency check FIRST — a replay must never consume an invoice
///     number or touch stock.
///  2. All validation and price resolution BEFORE the transaction opens,
///     so the transaction (which holds row locks on Stock) is as short as
///     possible. Long transactions on the checkout hot path are how a busy
///     store deadlocks itself.
///  3. Invoice number reserved outside the transaction — its reservation
///     commits independently by design (Architecture Review §4); a rolled
///     back sale burns the number, which is accepted and documented.
///  4. Inside the transaction: atomic stock decrements first (the operation
///     most likely to fail), then the invoice graph, then one SaveChanges,
///     then commit.
///
/// PRICING ASSUMPTION (was not explicitly locked in the Architecture
/// Review, stated here rather than buried): ProductBranch.SellingPrice is
/// the price of ONE BASE UNIT. Selling in a larger unit multiplies by that
/// unit's ConversionFactorToBase. Since the locked model gives a
/// ProductBranch exactly one price, deriving other units from the base is
/// the only internally consistent reading. If the business needs genuinely
/// independent per-unit prices (a case priced cheaper per item than singles),
/// that is a model change — an extra price column on ProductUnit or a
/// per-unit price table — not something to fudge here.
///
/// COSTING: still no FIFO/weighted-average (Architecture Review §12). For
/// batch-tracked products the caller states which batch is being sold; the
/// system does not choose. Auto-selection would be an implicit costing
/// policy, which the brief explicitly forbids inventing.
/// </summary>
public sealed class CompleteSaleHandler
{
    private readonly IApplicationDbContext _context;
    private readonly IDocumentNumberGenerator _documentNumberGenerator;
    private readonly IStockOperations _stockOperations;
    private readonly ITransactionalExecutor _transactionalExecutor;
    private readonly IPosPolicyService _posPolicyService;
    private readonly ISettingsProvider _settingsProvider;
    private readonly ICurrentUserContext _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly INotificationDispatcher _notificationDispatcher;

    public CompleteSaleHandler(
        IApplicationDbContext context,
        IDocumentNumberGenerator documentNumberGenerator,
        IStockOperations stockOperations,
        ITransactionalExecutor transactionalExecutor,
        IPosPolicyService posPolicyService,
        ISettingsProvider settingsProvider,
        ICurrentUserContext currentUser,
        IDateTimeProvider dateTimeProvider,
        INotificationDispatcher notificationDispatcher)
    {
        _context = context;
        _documentNumberGenerator = documentNumberGenerator;
        _stockOperations = stockOperations;
        _transactionalExecutor = transactionalExecutor;
        _posPolicyService = posPolicyService;
        _settingsProvider = settingsProvider;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
        _notificationDispatcher = notificationDispatcher;
    }

    public async Task<Result<CompleteSaleResponse>> HandleAsync(
        CompleteSaleCommand command,
        CancellationToken cancellationToken)
    {
        var validationError = CompleteSaleValidator.Validate(command);
        if (validationError is not null)
        {
            return Result.Failure<CompleteSaleResponse>(validationError);
        }

        // --- 1. Idempotency: has this exact sale already been recorded? ---
        var replay = await _context.SaleInvoices
            .AsNoTracking()
            .Where(s => s.ClientRequestId == command.ClientRequestId)
            .Select(s => new { s.Id, s.InvoiceNumber, s.TotalAmount, s.TotalPaidAmount })
            .FirstOrDefaultAsync(cancellationToken);

        if (replay is not null)
        {
            // Returning the ORIGINAL sale, not an error: from the cashier's
            // point of view the operation succeeded (it did, the first time),
            // and surfacing a failure would invite them to ring it up again.
            return Result.Success(new CompleteSaleResponse(
                replay.Id, replay.InvoiceNumber, replay.TotalAmount, replay.TotalPaidAmount,
                WasReplay: true, ReviewFlags: Array.Empty<string>()));
        }

        var branchExists = await _context.Branches.AsNoTracking()
            .AnyAsync(b => b.Id == command.BranchId, cancellationToken);
        if (!branchExists)
        {
            return Result.Failure<CompleteSaleResponse>(
                Error.NotFound("Sale.BranchNotFound", $"Branch '{command.BranchId}' was not found."));
        }

        // --- 2. Resolve customer snapshot (historical truth, §11) ---
        string? customerName = null;
        string? customerPhone = null;
        if (command.CustomerId is { } customerId)
        {
            var customer = await _context.Customers.AsNoTracking()
                .Where(c => c.Id == customerId)
                .Select(c => new { c.FullName, c.Phone })
                .FirstOrDefaultAsync(cancellationToken);

            if (customer is null)
            {
                return Result.Failure<CompleteSaleResponse>(
                    Error.NotFound("Sale.CustomerNotFound", $"Customer '{customerId}' was not found."));
            }

            // Snapshotted so a later change to the customer record cannot
            // rewrite what this invoice says (Architecture Review §4/§15).
            customerName = customer.FullName;
            customerPhone = customer.Phone;
        }

        // --- 3. Resolve units and server-side prices ---
        var unitIds = command.Items.Select(i => i.ProductUnitId).Distinct().ToList();
        var units = await _context.ProductUnits.AsNoTracking()
            .Where(u => unitIds.Contains(u.Id))
            .Select(u => new { u.Id, u.ProductId, u.ConversionFactorToBase })
            .ToDictionaryAsync(u => u.Id, cancellationToken);

        var productIds = command.Items.Select(i => i.ProductId).Distinct().ToList();

        var productBranches = await _context.ProductBranches.AsNoTracking()
            .Where(pb => pb.BranchId == command.BranchId && productIds.Contains(pb.ProductId))
            .Select(pb => new { pb.ProductId, pb.SellingPrice, pb.IsAvailableForSale })
            .ToDictionaryAsync(pb => pb.ProductId, cancellationToken);

        var products = await _context.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name, p.Status, p.IsBatchTracked })
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var reviewFlags = new List<string>();
        var resolvedLines = new List<ResolvedSaleLine>();

        foreach (var item in command.Items)
        {
            if (!products.TryGetValue(item.ProductId, out var product))
            {
                return Result.Failure<CompleteSaleResponse>(
                    Error.NotFound("Sale.ProductNotFound", $"Product '{item.ProductId}' was not found."));
            }

            if (product.Status != Domain.Catalog.ProductStatus.Active)
            {
                return Result.Failure<CompleteSaleResponse>(
                    Error.BusinessRule("Sale.ProductNotActive", $"Product '{product.Name}' is not active and cannot be sold."));
            }

            if (!units.TryGetValue(item.ProductUnitId, out var unit))
            {
                return Result.Failure<CompleteSaleResponse>(
                    Error.NotFound("Sale.UnitNotFound", $"Unit '{item.ProductUnitId}' was not found."));
            }

            if (unit.ProductId != item.ProductId)
            {
                return Result.Failure<CompleteSaleResponse>(
                    Error.Validation("Sale.UnitProductMismatch", $"Unit '{item.ProductUnitId}' does not belong to product '{product.Name}'."));
            }

            if (!productBranches.TryGetValue(item.ProductId, out var productBranch))
            {
                return Result.Failure<CompleteSaleResponse>(
                    Error.BusinessRule(
                        "Sale.ProductNotPricedAtBranch",
                        $"Product '{product.Name}' has no price at this branch. It must be onboarded before it can be sold."));
            }

            if (!productBranch.IsAvailableForSale)
            {
                return Result.Failure<CompleteSaleResponse>(
                    Error.BusinessRule("Sale.ProductNotAvailableAtBranch", $"Product '{product.Name}' is not available for sale at this branch."));
            }

            if (product.IsBatchTracked && item.ProductBatchId is null)
            {
                return Result.Failure<CompleteSaleResponse>(
                    Error.Validation("Sale.BatchRequired", $"Product '{product.Name}' is batch-tracked; a batch must be specified."));
            }

            if (!product.IsBatchTracked && item.ProductBatchId is not null)
            {
                return Result.Failure<CompleteSaleResponse>(
                    Error.Validation("Sale.BatchNotApplicable", $"Product '{product.Name}' is not batch-tracked; no batch may be specified."));
            }

            // Price derived server-side. See the PRICING ASSUMPTION note.
            var unitPrice = productBranch.SellingPrice * unit.ConversionFactorToBase;
            var grossLineTotal = unitPrice * item.Quantity;

            if (item.ManualDiscountAmount > 0)
            {
                if (item.ManualDiscountAmount > grossLineTotal)
                {
                    return Result.Failure<CompleteSaleResponse>(
                        Error.BusinessRule("Sale.DiscountExceedsLineTotal", $"Discount on '{product.Name}' exceeds the line total."));
                }

                // POLICY GATE — the first place IPosPolicyService actually
                // runs in anger. Decided instantly from cached settings; a
                // denial is a final answer, never a wait-for-manager state
                // (Architecture Review §13/§16.7).
                var decision = await _posPolicyService.EvaluateAsync(
                    PosOperation.ManualDiscount, item.ManualDiscountAmount, grossLineTotal, cancellationToken);

                if (!decision.IsAllowed)
                {
                    return Result.Failure<CompleteSaleResponse>(
                        Error.Forbidden("Sale.ManualDiscountDenied", decision.Reason ?? "Manual discount is not permitted."));
                }

                if (decision.RequiresReview && decision.Reason is not null)
                {
                    reviewFlags.Add(decision.Reason);
                }
            }

            resolvedLines.Add(new ResolvedSaleLine(
                item.ProductId,
                item.ProductUnitId,
                item.ProductBatchId,
                item.Quantity,
                item.Quantity * unit.ConversionFactorToBase,
                unitPrice,
                item.ManualDiscountAmount));
        }

        // --- 4. Totals, then the payment-completeness rule ---
        var itemsTotal = resolvedLines.Sum(l => l.LineTotal);
        var invoiceTotal = itemsTotal - command.InvoiceLevelDiscountAmount;

        if (command.InvoiceLevelDiscountAmount > 0)
        {
            var decision = await _posPolicyService.EvaluateAsync(
                PosOperation.ManualDiscount, command.InvoiceLevelDiscountAmount, itemsTotal, cancellationToken);

            if (!decision.IsAllowed)
            {
                return Result.Failure<CompleteSaleResponse>(
                    Error.Forbidden("Sale.ManualDiscountDenied", decision.Reason ?? "Manual discount is not permitted."));
            }

            if (decision.RequiresReview && decision.Reason is not null)
            {
                reviewFlags.Add(decision.Reason);
            }
        }

        if (invoiceTotal < 0)
        {
            return Result.Failure<CompleteSaleResponse>(
                Error.BusinessRule("Sale.NegativeTotal", "Discounts cannot reduce the invoice total below zero."));
        }

        var paymentsTotal = command.Payments.Sum(p => p.Amount);

        // A completed POS sale must be settled exactly. Underpayment would
        // silently create an unrecorded receivable; overpayment is rejected
        // by SaleInvoice.AddPayment anyway. Change given to the customer is
        // a till concern, not an invoice one — the invoice records what the
        // sale was worth and what was applied to it.
        if (paymentsTotal != invoiceTotal)
        {
            return Result.Failure<CompleteSaleResponse>(
                Error.BusinessRule(
                    "Sale.PaymentsDoNotSettleTotal",
                    $"Payments total {paymentsTotal} does not settle the invoice total {invoiceTotal}."));
        }

        // --- 5. Payment methods must exist and be active ---
        var paymentMethodIds = command.Payments.Select(p => p.PaymentMethodId).Distinct().ToList();
        var paymentMethods = await _context.PaymentMethods.AsNoTracking()
            .Where(pm => paymentMethodIds.Contains(pm.Id))
            .Select(pm => new { pm.Id, pm.Name, pm.IsActive, pm.AffectsCashDrawer, pm.RequiresExternalReference })
            .ToDictionaryAsync(pm => pm.Id, cancellationToken);

        foreach (var payment in command.Payments)
        {
            if (!paymentMethods.TryGetValue(payment.PaymentMethodId, out var method))
            {
                return Result.Failure<CompleteSaleResponse>(
                    Error.NotFound("Sale.PaymentMethodNotFound", $"Payment method '{payment.PaymentMethodId}' was not found."));
            }

            // Deactivated methods disappear from the cashier's choices but
            // historical invoices keep showing them (§16.11) — this check
            // governs NEW payments only.
            if (!method.IsActive)
            {
                return Result.Failure<CompleteSaleResponse>(
                    Error.BusinessRule("Sale.PaymentMethodInactive", $"Payment method '{method.Name}' is no longer active."));
            }

            if (method.RequiresExternalReference && string.IsNullOrWhiteSpace(payment.ExternalReference))
            {
                return Result.Failure<CompleteSaleResponse>(
                    Error.Validation(
                        "Sale.ExternalReferenceRequired",
                        $"Payment method '{method.Name}' requires an external reference (terminal/transfer id)."));
            }
        }

        // --- 6. حجز رقم الفاتورة (يلتزم بشكل مستقل عن باقي المعاملة، §4) ---
        var invoiceNumber = await _documentNumberGenerator.GetNextNumberAsync(
            command.BranchId, DocumentType.SaleInvoice, cancellationToken);

        var actorUserId = _currentUser.UserId ?? User.SystemUserId;
        var occurredAtUtc = _dateTimeProvider.UtcNow;

        // قراءة إعداد "السماح بالمخزون السالب" مرة وحدة قبل بداية المعاملة.
        // القرار: البيع ما يتوقف أبدًا بسبب نقص المخزون بالنظام (البضاعة
        // ممكن تكون وصلت فعليًا والفاتورة لسه ما دخلت)، إلا لو الأدمن طفّى
        // هذا الإعداد صراحة.
        var allowNegativeStock = await _settingsProvider.GetBoolAsync(
            InventorySettingsKeys.AllowNegativeStock, defaultValue: true, cancellationToken);

        // --- 7. الجزء الذري: كل هذا بمعاملة قاعدة بيانات واحدة ---
        var result = await _transactionalExecutor.ExecuteAsync<CompleteSaleResponse>(async ct =>
        {
            // نخصم المخزون أول شي — هو العملية الأكثر احتمالًا للفشل، وفشلها
            // قبل أي إضافات بيخلي التراجع (rollback) رخيص وسريع.
            foreach (var line in resolvedLines)
            {
                var outcome = await _stockOperations.TryDecreaseAsync(
                    line.ProductId, command.BranchId, line.ProductBatchId, line.QuantityBase,
                    allowNegativeStock, ct);

                switch (outcome)
                {
                    case StockDecrementOutcome.Succeeded:
                        // المسار الطبيعي — ما في شي إضافي مطلوب.
                        break;

                    case StockDecrementOutcome.SucceededWentNegative:
                        // البيع كمل عادي، بس نعلّمه للمراجعة الإدارية لاحقًا —
                        // نفس فلسفة الخصم اليدوي المرتفع: نسمح، بس نخلّيه
                        // مرئي، بلا ما نوقف الكاشير أو نطلب موافقة فورية.
                        reviewFlags.Add(
                            $"المنتج '{products[line.ProductId].Name}' بيع برصيد سالب بالمخزون (الكمية بالنظام غير كافية).");
                        break;

                    case StockDecrementOutcome.Failed:
                        // هذا بيصير بس لو إعداد AllowNegativeStock مطفي —
                        // رجعنا للسلوك الأصلي (المنع الافتراضي).
                        var productName = products[line.ProductId].Name;
                        return Result.Failure<CompleteSaleResponse>(
                            Error.BusinessRule(
                                "Sale.InsufficientStock",
                                $"المخزون غير كافٍ للمنتج '{productName}' بهذا الفرع."));

                    default:
                        throw new InvalidOperationException($"قيمة StockDecrementOutcome غير متوقعة: {outcome}");
                }
            }

            var invoice = new SaleInvoice(
                command.BranchId, invoiceNumber, command.ClientRequestId,
                command.CustomerId, customerName, customerPhone);

            var movements = new List<StockMovement>();

            foreach (var line in resolvedLines)
            {
                // DiscountId is null: an ad-hoc checkout discount, which is
                // precisely how the manual-discount report identifies it
                // (§13.6) — the absence of a rule reference IS the signal.
                var invoiceItem = invoice.AddItem(
                    line.ProductId, line.ProductUnitId, line.Quantity,
                    line.UnitPrice, line.ManualDiscountAmount, discountId: null);

                movements.Add(new StockMovement(
                    line.ProductId,
                    command.BranchId,
                    line.ProductUnitId,
                    line.ProductBatchId,
                    line.QuantityBase,
                    MovementType.SaleOut,
                    reason: null,
                    occurredAtUtc,
                    actorUserId,
                    StockMovementReferenceType.SaleInvoiceItem,
                    invoiceItem.Id));
            }

            if (command.InvoiceLevelDiscountAmount > 0)
            {
                invoice.ApplyInvoiceLevelDiscount(discountId: null, command.InvoiceLevelDiscountAmount);
            }

            var cashMovements = new List<CashDrawerLog>();

            foreach (var paymentDto in command.Payments)
            {
                // AddPayment enforces the over-payment invariant in-memory
                // (TotalPaidAmount + amount <= TotalAmount). For a sale built
                // in one shot like this, that is sufficient and authoritative
                // — the invoice does not exist concurrently anywhere else
                // yet. The raw conditional UPDATE guard described in §16.8
                // becomes the operative one when payments are added to an
                // already-persisted invoice, which is a later use case.
                var payment = invoice.AddPayment(
                    paymentDto.PaymentMethodId,
                    paymentDto.Amount,
                    actorUserId,
                    command.BranchId,
                    paymentDto.ExternalReference,
                    paymentDto.ClientRequestId);

                // Cash impact is decided by the payment method's BEHAVIOUR
                // flag, never by its name or code (§16.2).
                if (paymentMethods[paymentDto.PaymentMethodId].AffectsCashDrawer)
                {
                    cashMovements.Add(new CashDrawerLog(
                        command.BranchId,
                        CashDrawerMovementType.SaleCashIn,
                        paymentDto.Amount,
                        // References the specific payment, not the invoice —
                        // this is what makes the audit chain
                        // SaleInvoice → SaleInvoicePayment → CashDrawerLog
                        // precise rather than stopping at the header (§16.6).
                        CashDrawerReferenceType.SaleInvoicePayment,
                        payment.Id,
                        actorUserId,
                        occurredAtUtc));
                }
            }

            _context.SaleInvoices.Add(invoice);
            _context.StockMovements.AddRange(movements);
            _context.CashDrawerLogs.AddRange(cashMovements);

            await _context.SaveChangesAsync(ct);

            return Result.Success(new CompleteSaleResponse(
                invoice.Id, invoice.InvoiceNumber, invoice.TotalAmount, invoice.TotalPaidAmount,
                WasReplay: false, reviewFlags));
        }, cancellationToken);

        // === إرسال تنبيه بعد التزام المعاملة فعليًا، لا أثناءها ===
        // بالضبط حسب التحذير الموثَّق بـNotificationDispatcher — لو أرسلنا
        // من جوّا الـdelegate فوق وصار rollback بعدها لسبب لاحق، كان رح
        // يوصل تنبيه حقيقي عن بيعة ما التزمت فعليًا بقاعدة البيانات.
        if (result.IsSuccess && result.Value.ReviewFlags.Count > 0)
        {
            var flagsText = string.Join("\n- ", result.Value.ReviewFlags);
            await _notificationDispatcher.NotifyAsync(
                $"مراجعة مطلوبة — فاتورة {result.Value.InvoiceNumber}",
                $"- {flagsText}",
                cancellationToken);
        }

        return result;
    }

    private sealed record ResolvedSaleLine(
        Guid ProductId,
        Guid ProductUnitId,
        Guid? ProductBatchId,
        decimal Quantity,
        decimal QuantityBase,
        decimal UnitPrice,
        decimal ManualDiscountAmount)
    {
        public decimal LineTotal => (UnitPrice * Quantity) - ManualDiscountAmount;
    }
}
