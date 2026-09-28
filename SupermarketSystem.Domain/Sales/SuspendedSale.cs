using SupermarketSystem.Domain.Common;

namespace SupermarketSystem.Domain.Sales;

/// <summary>
/// Aggregate root, branch-owned, user-owned. A parked cart — pre-
/// transactional, not yet a completed financial event, so it is NOT subject
/// to the "never delete financial history" rule; it can be deleted once
/// resumed/converted into a real SaleInvoice or abandoned.
/// SuspendedSaleItem was a missing entity in the original list (only the
/// header was named) — added in Architecture Review §4.
/// </summary>
public enum SuspendedSaleStatus
{
    /// <summary>جاهز، بانتظار الكاشير يحاسب عليه.</summary>
    Open = 1,
    /// <summary>الكاشير حاسب عليه - SaleInvoiceId هي الفاتورة الفعلية.</summary>
    Completed = 2,
    Cancelled = 3
}

/// <summary>
/// "طلب جاهز" من مساعد الكاشير (28/9/2026): المساعد/الشريك بيضرب أغراض الزبون من تلفونه وبيطلعله
/// رقم طلب قصير، والكاشير بينزّل الطلب بالسلة وبيحاسب هو على المصاري الحقيقية. ما في أي أثر مالي أو
/// مخزني هون - كله بيصير وقت البيع الفعلي. ما بينحذف بعد البيع (Status بدل الحذف) عشان رقم الطلب ما
/// يتكرر بنفس اليوم، وعشان يضل معروف مين جهّز الطلب (UserId).
/// </summary>
public class SuspendedSale : AuditableEntity, IBranchOwned
{
    public const int MaxNoteLength = 200;

    public Guid BranchId { get; private set; }

    /// <summary>مين جهّز الطلب (المساعد/الشريك).</summary>
    public Guid UserId { get; private set; }

    /// <summary>رقم قصير بيتحكى للزبون/الكاشير ("طلب 17") - بيبلش من 1 كل يوم (UTC) لكل فرع.</summary>
    public int TicketNumber { get; private set; }
    public DateOnly TicketDateUtc { get; private set; }
    public string? Note { get; private set; }
    public SuspendedSaleStatus Status { get; private set; }
    public Guid? SaleInvoiceId { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }

    private readonly List<SuspendedSaleItem> _items = new();
    public IReadOnlyCollection<SuspendedSaleItem> Items => _items.AsReadOnly();

    private SuspendedSale() { } // EF Core

    public SuspendedSale(Guid branchId, Guid userId, int ticketNumber, DateOnly ticketDateUtc, string? note)
    {
        if (ticketNumber < 1)
        {
            throw new DomainException("Ticket number must be positive.");
        }

        BranchId = branchId;
        UserId = userId;
        TicketNumber = ticketNumber;
        TicketDateUtc = ticketDateUtc;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        Status = SuspendedSaleStatus.Open;
    }

    public void Complete(Guid saleInvoiceId, DateTime closedAtUtc)
    {
        if (Status != SuspendedSaleStatus.Open)
        {
            throw new DomainException("Only an open prepared order can be completed.");
        }

        Status = SuspendedSaleStatus.Completed;
        SaleInvoiceId = saleInvoiceId;
        ClosedAtUtc = closedAtUtc;
    }

    public void Cancel(DateTime closedAtUtc)
    {
        if (Status != SuspendedSaleStatus.Open)
        {
            throw new DomainException("Only an open prepared order can be cancelled.");
        }

        Status = SuspendedSaleStatus.Cancelled;
        ClosedAtUtc = closedAtUtc;
    }

    public SuspendedSaleItem AddItem(Guid productId, Guid productUnitId, decimal quantity, decimal unitPriceSnapshot)
    {
        var item = new SuspendedSaleItem(Id, productId, productUnitId, quantity, unitPriceSnapshot);
        _items.Add(item);
        return item;
    }
}

public class SuspendedSaleItem : Entity
{
    public Guid SuspendedSaleId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid ProductUnitId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal UnitPriceSnapshot { get; private set; }

    private SuspendedSaleItem() { } // EF Core

    internal SuspendedSaleItem(Guid suspendedSaleId, Guid productId, Guid productUnitId, decimal quantity, decimal unitPriceSnapshot)
    {
        SuspendedSaleId = suspendedSaleId;
        ProductId = productId;
        ProductUnitId = productUnitId;
        Quantity = quantity;
        UnitPriceSnapshot = unitPriceSnapshot;
    }
}
