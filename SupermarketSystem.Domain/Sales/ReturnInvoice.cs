using SupermarketSystem.Domain.Common;

namespace SupermarketSystem.Domain.Sales;

public enum ReturnReason
{
    Defective = 1,
    CustomerChangedMind = 2,
    WrongItem = 3,
    Expired = 4,
    Other = 5
}

/// <summary>
/// Aggregate root, branch-owned, transaction-owned. Restrict FK back to the
/// original SaleInvoice (cannot delete a sale that has a return against
/// it). Immutable once completed, drives StockMovement (MovementType.ReturnIn)
/// and the Stock increment in the same transaction (Application/
/// Infrastructure concern, not modeled here). TotalRefundedAmount mirrors
/// SaleInvoice.TotalPaidAmount — same atomic-guard technique, fourth
/// application of the same pattern (§16.9).
/// </summary>
public class ReturnInvoice : AuditableEntity, IBranchOwned, IHasRowVersion
{
    public Guid BranchId { get; private set; }
    public string InvoiceNumber { get; private set; } = null!;
    public Guid OriginalSaleInvoiceId { get; private set; }

    /// <summary>
    /// مفتاح idempotency من العميل، unique — يمنع إنشاء إرجاعين حقيقيين
    /// من ضغطة مزدوجة أو إعادة إرسال بعد انقطاع شبكة. نفس آلية
    /// SaleInvoice.ClientRequestId بالضبط، ولنفس السبب: الإرجاع عملية
    /// مالية تُنفَّذ مرة وحدة، وتكرارها بيرجّع بضاعة وفلوس مرتين.
    /// </summary>
    public Guid ClientRequestId { get; private set; }

    /// <summary>
    /// علامة "راجعتها" الإدارية — تُضاف *بعد* اكتمال الإرجاع بوقت، ولا
    /// تغيّر أي شيء مالي ولا حالة الإرجاع نفسه. الإرجاع بيضل ظاهر
    /// كإرجاع للأبد بسجل المرتجعات؛ هذي بس معلومة إضافية إن صاحب المحل
    /// شافها وراجعها.
    ///
    /// هذا *ليس* موافقة مسبقة — الكاشير أنهى العملية فورًا والزبون أخذ
    /// حقه وقتها. متسق تمامًا مع فلسفة النظام: نفّذ فورًا، سجّل، صنّف،
    /// راجع لاحقًا.
    /// </summary>
    public DateTime? ReviewedAtUtc { get; private set; }
    public Guid? ReviewedByUserId { get; private set; }
    public ReturnReason Reason { get; private set; }
    public string? Notes { get; private set; }
    public decimal TotalAmount { get; private set; }
    public decimal TotalRefundedAmount { get; private set; }
    public byte[]? RowVersion { get; private set; }

    private readonly List<ReturnInvoiceItem> _items = new();
    public IReadOnlyCollection<ReturnInvoiceItem> Items => _items.AsReadOnly();

    private readonly List<ReturnInvoicePayment> _payments = new();
    public IReadOnlyCollection<ReturnInvoicePayment> Payments => _payments.AsReadOnly();

    private ReturnInvoice() { } // EF Core

    public ReturnInvoice(Guid branchId, string invoiceNumber, Guid clientRequestId, Guid originalSaleInvoiceId, ReturnReason reason, string? notes)
    {
        if (clientRequestId == Guid.Empty)
        {
            throw new DomainException("A client request id is required; it is the return's idempotency key.");
        }

        ClientRequestId = clientRequestId;
        BranchId = branchId;
        InvoiceNumber = invoiceNumber;
        OriginalSaleInvoiceId = originalSaleInvoiceId;
        Reason = reason;
        Notes = notes;
        TotalAmount = 0;
        TotalRefundedAmount = 0;
    }

    public ReturnInvoiceItem AddItem(Guid saleInvoiceItemId, Guid productId, Guid productUnitId, decimal quantity, decimal unitPriceSnapshot)
    {
        var item = new ReturnInvoiceItem(Id, saleInvoiceItemId, productId, productUnitId, quantity, unitPriceSnapshot);
        _items.Add(item);
        TotalAmount += item.LineTotal;
        return item;
    }

    /// <summary>
    /// In-memory guard mirroring SaleInvoice.AddPayment — the raw atomic
    /// conditional UPDATE (TotalRefundedAmount + @amount &lt;= TotalAmount)
    /// is the enforcement point for concurrent/incremental refund additions
    /// (§16.9); this is defense in depth for the single-transaction path.
    /// </summary>
    public ReturnInvoicePayment AddPayment(Guid paymentMethodId, decimal amount, Guid userId, Guid branchId, string? externalReference, Guid clientRequestId)
    {
        if (TotalRefundedAmount + amount > TotalAmount)
        {
            throw new DomainException("This refund would cause total refunds to exceed the return total.");
        }

        var payment = new ReturnInvoicePayment(Id, paymentMethodId, amount, userId, branchId, externalReference, clientRequestId);
        _payments.Add(payment);
        TotalRefundedAmount += amount;
        return payment;
    }

    /// <summary>
    /// تُستدعى مرة وحدة فقط — إعادة وضع العلامة على إرجاع مُراجَع أصلًا
    /// بترفض، عشان تاريخ/منفِّذ المراجعة الأصلي ما ينكتب فوقه بالغلط.
    /// </summary>
    public void MarkReviewed(Guid reviewedByUserId, DateTime reviewedAtUtc)
    {
        if (ReviewedAtUtc is not null)
        {
            throw new DomainException("This return has already been marked as reviewed.");
        }

        ReviewedByUserId = reviewedByUserId;
        ReviewedAtUtc = reviewedAtUtc;
    }
}

/// <summary>
/// Child of the ReturnInvoice aggregate. SaleInvoiceItemId ties each return
/// line back to the specific original sale line it's returning against —
/// what SaleInvoiceItem.RecordReturn's quantity guard is checked against.
/// </summary>
public class ReturnInvoiceItem : Entity
{
    public Guid ReturnInvoiceId { get; private set; }
    public Guid SaleInvoiceItemId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid ProductUnitId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal UnitPriceSnapshot { get; private set; }
    public decimal LineTotal { get; private set; }

    private ReturnInvoiceItem() { } // EF Core

    internal ReturnInvoiceItem(Guid returnInvoiceId, Guid saleInvoiceItemId, Guid productId, Guid productUnitId, decimal quantity, decimal unitPriceSnapshot)
    {
        if (quantity <= 0)
        {
            throw new DomainException("Return quantity must be positive.");
        }

        ReturnInvoiceId = returnInvoiceId;
        SaleInvoiceItemId = saleInvoiceItemId;
        ProductId = productId;
        ProductUnitId = productUnitId;
        Quantity = quantity;
        UnitPriceSnapshot = unitPriceSnapshot;
        LineTotal = quantity * unitPriceSnapshot;
    }
}
