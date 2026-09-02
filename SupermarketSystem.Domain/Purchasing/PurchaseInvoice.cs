using SupermarketSystem.Domain.Common;

namespace SupermarketSystem.Domain.Purchasing;

public enum PurchaseInvoiceStatus
{
    Draft = 1,
    Received = 2,
    Cancelled = 3
}

/// <summary>
/// Aggregate root, branch-owned, transaction-owned. Once Received, its
/// financial content (items, quantities, costs) is historical and
/// immutable — the same "immutable once completed" rule applied to
/// SaleInvoice (Architecture Review §15) applies here.
/// InvoiceNumber is reserved via BranchDocumentSequence
/// (DocumentType.PurchaseInvoice), never MAX+1, never a global IDENTITY.
/// </summary>
public class PurchaseInvoice : AuditableEntity, IBranchOwned, IHasRowVersion
{
    public Guid BranchId { get; private set; }
    public Guid SupplierId { get; private set; }
    public string InvoiceNumber { get; private set; } = null!;
    public string? SupplierInvoiceReference { get; private set; }
    public PurchaseInvoiceStatus Status { get; private set; }
    public decimal TotalAmount { get; private set; }

    /// <summary>
    /// عدّاد محفوظ (guarded running counter)، نفس نمط SaleInvoice.TotalPaidAmount
    /// بالضبط بس بالاتجاه المعاكس — هون "كم دفعنا نحن للمورد"، لا "كم دفع
    /// الزبون لنا". TotalAmount - TotalPaidAmount = المتبقي علينا (الدين).
    /// فحص ذري إضافي بطبقة Infrastructure (UPDATE...WHERE TotalPaidAmount +
    /// @amount &lt;= TotalAmount) هو الحماية الفعلية ضد إرسال مزدوج
    /// متزامن — هذا الميثود دفاع بعمق، لا نقطة الحماية الوحيدة.
    /// </summary>
    public decimal TotalPaidAmount { get; private set; }

    public byte[]? RowVersion { get; private set; }

    private readonly List<PurchaseInvoiceItem> _items = new();
    public IReadOnlyCollection<PurchaseInvoiceItem> Items => _items.AsReadOnly();

    private readonly List<PurchaseInvoicePayment> _payments = new();
    public IReadOnlyCollection<PurchaseInvoicePayment> Payments => _payments.AsReadOnly();

    private readonly List<PurchaseInvoiceImage> _images = new();
    public IReadOnlyCollection<PurchaseInvoiceImage> Images => _images.AsReadOnly();

    private PurchaseInvoice() { } // EF Core

    public PurchaseInvoice(Guid branchId, Guid supplierId, string invoiceNumber, string? supplierInvoiceReference)
    {
        BranchId = branchId;
        SupplierId = supplierId;
        InvoiceNumber = invoiceNumber;
        SupplierInvoiceReference = supplierInvoiceReference;
        Status = PurchaseInvoiceStatus.Draft;
        TotalAmount = 0;
        TotalPaidAmount = 0;
    }

    public PurchaseInvoiceItem AddItem(
        Guid productId, Guid productUnitId, Guid? productBatchId, decimal quantity, decimal unitCost, bool needsReview = false)
    {
        if (Status != PurchaseInvoiceStatus.Draft)
        {
            throw new DomainException("Cannot add items to a purchase invoice that is not Draft.");
        }

        var item = new PurchaseInvoiceItem(Id, productId, productUnitId, productBatchId, quantity, unitCost, needsReview);
        _items.Add(item);
        TotalAmount += item.LineTotal;
        return item;
    }

    public PurchaseInvoiceImage AddImage(string url)
    {
        var image = new PurchaseInvoiceImage(Id, url);
        _images.Add(image);
        return image;
    }

    public void MarkReceived()
    {
        if (Status != PurchaseInvoiceStatus.Draft)
        {
            throw new DomainException("Only a Draft purchase invoice can be marked Received.");
        }

        if (_items.Count == 0)
        {
            throw new DomainException("Cannot receive a purchase invoice with no items.");
        }

        Status = PurchaseInvoiceStatus.Received;
    }

    /// <summary>
    /// يسجّل دفعة فعلية للمورد على هذه الفاتورة. يقبل الدفع بأي حالة
    /// (Draft أو Received) — عمليًا شائع تدفع دفعة مقدَّمة قبل ما توصل
    /// البضاعة أصلًا، فالقيد هون بس "لا تتجاوز المبلغ الكلي"، لا حالة
    /// معيّنة.
    /// </summary>
    public PurchaseInvoicePayment AddPayment(Guid paymentMethodId, decimal amount, Guid userId, Guid branchId, string? externalReference, Guid clientRequestId)
    {
        if (TotalPaidAmount + amount > TotalAmount)
        {
            throw new DomainException("This payment would cause total payments to exceed the invoice total.");
        }

        var payment = new PurchaseInvoicePayment(Id, paymentMethodId, amount, userId, branchId, externalReference, clientRequestId);
        _payments.Add(payment);
        TotalPaidAmount += amount;
        return payment;
    }

    public void Cancel()
    {
        if (Status == PurchaseInvoiceStatus.Received)
        {
            throw new DomainException("A received purchase invoice cannot be cancelled; historical records are preserved, not deleted.");
        }

        Status = PurchaseInvoiceStatus.Cancelled;
    }
}

/// <summary>
/// Child of the PurchaseInvoice aggregate (Cascade delete — نفس معاملة
/// PurchaseInvoiceItem). نفس بنية SaleInvoicePayment بالضبط بالاتجاه
/// المعاكس (نحن ندفع للمورد، لا الزبون يدفع لنا) — بلا آلية عكس (Reverse)
/// عمدًا: تعديل خطأ بدفعة مورد حالة أندر بكثير من إلغاء بيع لزبون، ولو
/// احتجناها لاحقًا، سهل تُضاف بنفس نمط SaleInvoicePayment.Reverse().
///
/// ClientRequestId مفتاح idempotency — نفس السبب بالضبط زي SaleInvoicePayment:
/// إرسال مزدوج (فشل شبكة، دبل كليك) يترفض بدل ما يسجّل دفعة مكرّرة.
/// </summary>
public class PurchaseInvoicePayment : Entity, IBranchOwned, IHasRowVersion
{
    public Guid PurchaseInvoiceId { get; private set; }
    public Guid PaymentMethodId { get; private set; }
    public decimal Amount { get; private set; }
    public Guid UserId { get; private set; }
    public Guid BranchId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public string? ExternalReference { get; private set; }
    public Guid ClientRequestId { get; private set; }

    public byte[]? RowVersion { get; private set; }

    private PurchaseInvoicePayment() { } // EF Core

    internal PurchaseInvoicePayment(Guid purchaseInvoiceId, Guid paymentMethodId, decimal amount, Guid userId, Guid branchId, string? externalReference, Guid clientRequestId)
    {
        if (amount <= 0)
        {
            throw new DomainException("Payment amount must be positive.");
        }

        PurchaseInvoiceId = purchaseInvoiceId;
        PaymentMethodId = paymentMethodId;
        Amount = amount;
        UserId = userId;
        BranchId = branchId;
        CreatedAtUtc = DateTime.UtcNow;
        ExternalReference = externalReference;
        ClientRequestId = clientRequestId;
    }
}

/// <summary>
/// Child of the PurchaseInvoice aggregate. UnitCost/Quantity/LineTotal are
/// a historical snapshot, fixed once the invoice is Received.
/// </summary>
public class PurchaseInvoiceItem : Entity
{
    public Guid PurchaseInvoiceId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid ProductUnitId { get; private set; }
    public Guid? ProductBatchId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal UnitCost { get; private set; }
    public decimal LineTotal { get; private set; }

    /// <summary>
    /// "سماح مع مراجعة" - نفس نمط StockMovement.NeedsReview بالضبط، بس
    /// لسطر شراء سعره أعلى بنسبة ملحوظة عن متوسط آخر عمليات شراء لنفس
    /// المنتج (راجع CompletePurchaseInvoiceHandler). لا نمنع الفاتورة،
    /// بس نعلّم السطر ليظهر بقائمة المراجعات الموحَّدة.
    /// </summary>
    public bool NeedsReview { get; private set; }
    public DateTime? ReviewedAtUtc { get; private set; }
    public Guid? ReviewedByUserId { get; private set; }

    private PurchaseInvoiceItem() { } // EF Core

    internal PurchaseInvoiceItem(
        Guid purchaseInvoiceId, Guid productId, Guid productUnitId, Guid? productBatchId,
        decimal quantity, decimal unitCost, bool needsReview = false)
    {
        if (quantity <= 0)
        {
            throw new DomainException("Purchase quantity must be positive.");
        }

        PurchaseInvoiceId = purchaseInvoiceId;
        ProductId = productId;
        ProductUnitId = productUnitId;
        ProductBatchId = productBatchId;
        Quantity = quantity;
        UnitCost = unitCost;
        LineTotal = quantity * unitCost;
        NeedsReview = needsReview;
    }

    public void MarkReviewed(Guid reviewedByUserId, DateTime reviewedAtUtc)
    {
        if (ReviewedAtUtc is not null)
        {
            throw new DomainException("This item has already been reviewed.");
        }

        NeedsReview = false;
        ReviewedByUserId = reviewedByUserId;
        ReviewedAtUtc = reviewedAtUtc;
    }
}

public class PurchaseInvoiceImage : Entity
{
    public Guid PurchaseInvoiceId { get; private set; }
    public string Url { get; private set; } = null!;

    private PurchaseInvoiceImage() { } // EF Core

    internal PurchaseInvoiceImage(Guid purchaseInvoiceId, string url)
    {
        PurchaseInvoiceId = purchaseInvoiceId;
        Url = url;
    }
}
