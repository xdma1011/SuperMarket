using SupermarketSystem.Domain.Common;
using SupermarketSystem.Domain.Payments;

namespace SupermarketSystem.Domain.Sales;

/// <summary>
/// Valid transitions (Architecture Review §13.1): Completed -> Voided
/// (only while TotalReturnedAmount is zero), Completed -> PartiallyReturned,
/// Completed/PartiallyReturned -> FullyReturned. No transition leaves
/// Voided or FullyReturned. There is no persisted Draft state — per the
/// "operational continuity" principle, a SaleInvoice is created directly as
/// Completed; the in-progress cart phase is SuspendedSale, not a Draft
/// SaleInvoice row.
/// </summary>
public enum SaleInvoiceStatus
{
    Completed = 1,
    Voided = 2,
    PartiallyReturned = 3,
    FullyReturned = 4
}

public enum VoidReason
{
    CashierError = 1,
    CustomerCancelled = 2,
    SystemError = 3,
    Other = 4
}

/// <summary>
/// Aggregate root, branch-owned, transaction-owned. Once created (always as
/// Completed — see SaleInvoiceStatus remarks), its financial content is
/// permanently fixed (Architecture Review §15): Items' Quantity /
/// UnitPriceSnapshot / DiscountSnapshot / LineTotal, TotalAmount, and every
/// Payment row never change. The only things that change afterward are the
/// explicitly enumerated fields below (Status, Voided*, TotalReturnedAmount,
/// TotalPaidAmount, and SaleInvoiceItem.QuantityReturned) — each one a
/// projection of an immutable event elsewhere (a return completing, a void
/// being recorded), never a direct edit. InvoiceNumber is reserved via
/// BranchDocumentSequence (DocumentType.SaleInvoice) — never MAX+1, never a
/// global IDENTITY (Architecture Review §4).
/// </summary>
public class SaleInvoice : AuditableEntity, IBranchOwned, IHasRowVersion
{
    public Guid BranchId { get; private set; }
    public string InvoiceNumber { get; private set; } = null!;

    /// <summary>
    /// Client-generated idempotency key, unique-indexed (Architecture Review
    /// §16). This is what makes a double-clicked "complete sale" button or a
    /// network-retried request resolve to ONE invoice instead of two.
    ///
    /// Note this is a separate guarantee from SaleInvoicePayment.ClientRequestId:
    /// that one prevents a duplicate *payment* line, this one prevents a
    /// duplicate *sale*. A sale with no payments at all (fully discounted,
    /// or an account sale) would otherwise have no idempotency protection.
    /// </summary>
    public Guid ClientRequestId { get; private set; }

    public Guid? CustomerId { get; private set; }
    public string? CustomerNameSnapshot { get; private set; }
    public string? CustomerPhoneSnapshot { get; private set; }
    public SaleInvoiceStatus Status { get; private set; }
    public decimal TotalAmount { get; private set; }

    /// <summary>Optional whole-order discount rule reference, for traceability only (SetNull on delete) — §13.6.</summary>
    public Guid? DiscountId { get; private set; }
    public decimal DiscountAmountSnapshot { get; private set; }

    /// <summary>Guarded running counter — see §16.8. Never derived-and-displayed only; it's the subject of the over-payment atomicity guard.</summary>
    public decimal TotalPaidAmount { get; private set; }

    /// <summary>Guarded running counter, mirrors TotalPaidAmount — see §13.2.</summary>
    public decimal TotalReturnedAmount { get; private set; }

    public DateTime? VoidedAtUtc { get; private set; }
    public Guid? VoidedByUserId { get; private set; }
    public VoidReason? VoidReason { get; private set; }
    public string? VoidNotes { get; private set; }

    public byte[]? RowVersion { get; private set; }

    private readonly List<SaleInvoiceItem> _items = new();
    public IReadOnlyCollection<SaleInvoiceItem> Items => _items.AsReadOnly();

    private readonly List<SaleInvoicePayment> _payments = new();
    public IReadOnlyCollection<SaleInvoicePayment> Payments => _payments.AsReadOnly();

    private SaleInvoice() { } // EF Core

    public SaleInvoice(Guid branchId, string invoiceNumber, Guid clientRequestId, Guid? customerId, string? customerNameSnapshot, string? customerPhoneSnapshot)
    {
        if (clientRequestId == Guid.Empty)
        {
            throw new DomainException("A client request id is required; it is the sale's idempotency key.");
        }

        BranchId = branchId;
        InvoiceNumber = invoiceNumber;
        ClientRequestId = clientRequestId;
        CustomerId = customerId;
        CustomerNameSnapshot = customerNameSnapshot;
        CustomerPhoneSnapshot = customerPhoneSnapshot;
        Status = SaleInvoiceStatus.Completed;
        TotalAmount = 0;
        TotalPaidAmount = 0;
        TotalReturnedAmount = 0;
    }

    public SaleInvoiceItem AddItem(Guid productId, Guid productUnitId, decimal quantity, decimal unitPriceSnapshot, decimal discountSnapshot, Guid? discountId)
    {
        var item = new SaleInvoiceItem(Id, productId, productUnitId, quantity, unitPriceSnapshot, discountSnapshot, discountId);
        _items.Add(item);
        TotalAmount += item.LineTotal;
        return item;
    }

    public void ApplyInvoiceLevelDiscount(Guid? discountId, decimal discountAmountSnapshot)
    {
        DiscountId = discountId;
        DiscountAmountSnapshot = discountAmountSnapshot;
        TotalAmount -= discountAmountSnapshot;
    }

    /// <summary>
    /// In-memory guard for the common single-transaction "build the whole
    /// invoice graph, then save once" path. For payments added incrementally
    /// against an already-persisted invoice, Infrastructure additionally
    /// enforces this with a raw atomic conditional UPDATE
    /// (TotalPaidAmount + @amount &lt;= TotalAmount) — see §16.8 — which is
    /// what actually protects against concurrent double-submission; this
    /// method is defense in depth, not the sole enforcement point.
    /// </summary>
    public SaleInvoicePayment AddPayment(Guid paymentMethodId, decimal amount, Guid userId, Guid branchId, string? externalReference, Guid clientRequestId)
    {
        if (TotalPaidAmount + amount > TotalAmount)
        {
            throw new DomainException("This payment would cause total payments to exceed the invoice total.");
        }

        var payment = new SaleInvoicePayment(Id, paymentMethodId, amount, userId, branchId, externalReference, clientRequestId);
        _payments.Add(payment);
        TotalPaidAmount += amount;
        return payment;
    }

    /// <summary>
    /// Called by the return-completion workflow after a ReturnInvoice for
    /// this sale has been recorded — transitions Status and increments
    /// TotalReturnedAmount together. The per-item QuantityReturned guard
    /// (SaleInvoiceItem.RecordReturn) is applied per line before this is
    /// called for the header total.
    /// </summary>
    public void RegisterReturn(decimal returnedAmount, bool allItemsFullyReturned)
    {
        if (Status is SaleInvoiceStatus.Voided or SaleInvoiceStatus.FullyReturned)
        {
            throw new DomainException($"Cannot register a return against an invoice in status {Status}.");
        }

        TotalReturnedAmount += returnedAmount;
        Status = allItemsFullyReturned ? SaleInvoiceStatus.FullyReturned : SaleInvoiceStatus.PartiallyReturned;
    }

    /// <summary>
    /// Void permitted only from Completed with zero returns recorded yet —
    /// flagged assumption, Architecture Review §13.1.
    /// </summary>
    /// <summary>
    /// يعكس دفعة مكتملة *وينقص العدّاد بنفس العملية* — مقصود يكون على
    /// مستوى الـaggregate لا على SaleInvoicePayment مباشرة.
    ///
    /// السبب: الثابت الموثّق بالتصميم (§16.8) هو
    /// "مجموع الدفعات المكتملة == TotalPaidAmount". لو استدعى أي كود
    /// payment.Reverse() لحاله (وهي public)، الدفعة بتصير Reversed بينما
    /// TotalPaidAmount بيضل على قيمته القديمة — كسر صامت للثابت، ما بيبان
    /// إلا بتقرير غلط بعدين. هذا الميثود بيضمن إنه ما في طريقة تعكس دفعة
    /// بلا ما تحدّث العدّاد، لأنه الاثنين هون بنفس المكان.
    /// </summary>
    public void ReversePayment(Guid paymentId, Guid reversedByUserId, DateTime reversedAtUtc, string reason)
    {
        var payment = _payments.FirstOrDefault(p => p.Id == paymentId)
            ?? throw new DomainException($"Payment '{paymentId}' does not belong to this invoice.");

        payment.Reverse(reversedByUserId, reversedAtUtc, reason);
        TotalPaidAmount -= payment.Amount;
    }

    public void Void(Guid voidedByUserId, DateTime voidedAtUtc, VoidReason reason, string? notes)
    {
        if (Status != SaleInvoiceStatus.Completed)
        {
            throw new DomainException("Only a Completed invoice with no returns recorded can be voided.");
        }

        if (TotalReturnedAmount > 0)
        {
            throw new DomainException("An invoice with returns already recorded cannot be voided; it can only be further returned.");
        }

        Status = SaleInvoiceStatus.Voided;
        VoidedAtUtc = voidedAtUtc;
        VoidedByUserId = voidedByUserId;
        VoidReason = reason;
        VoidNotes = notes;
    }
}

/// <summary>
/// Child of the SaleInvoice aggregate. Quantity / UnitPriceSnapshot /
/// DiscountSnapshot / LineTotal never change after creation.
/// QuantityReturned is the one controlled mutable field — a running counter
/// mirroring how Stock relates to StockMovement (§13.2) — enforced via the
/// same atomic-conditional-update technique as the stock decrement when
/// applied concurrently (Infrastructure concern); the method below is the
/// in-memory-safe equivalent for single-transaction paths.
/// </summary>
public class SaleInvoiceItem : Entity
{
    public Guid SaleInvoiceId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid ProductUnitId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal UnitPriceSnapshot { get; private set; }
    public decimal DiscountSnapshot { get; private set; }
    public decimal LineTotal { get; private set; }
    public Guid? DiscountId { get; private set; }
    public decimal QuantityReturned { get; private set; }

    private SaleInvoiceItem() { } // EF Core

    internal SaleInvoiceItem(Guid saleInvoiceId, Guid productId, Guid productUnitId, decimal quantity, decimal unitPriceSnapshot, decimal discountSnapshot, Guid? discountId)
    {
        if (quantity <= 0)
        {
            throw new DomainException("Sale quantity must be positive.");
        }

        SaleInvoiceId = saleInvoiceId;
        ProductId = productId;
        ProductUnitId = productUnitId;
        Quantity = quantity;
        UnitPriceSnapshot = unitPriceSnapshot;
        DiscountSnapshot = discountSnapshot;
        DiscountId = discountId;
        LineTotal = (quantity * unitPriceSnapshot) - discountSnapshot;
        QuantityReturned = 0;
    }

    public void RecordReturn(decimal returnQuantity)
    {
        if (QuantityReturned + returnQuantity > Quantity)
        {
            throw new DomainException("Return quantity would exceed the quantity originally sold minus quantities already returned.");
        }

        QuantityReturned += returnQuantity;
    }
}
