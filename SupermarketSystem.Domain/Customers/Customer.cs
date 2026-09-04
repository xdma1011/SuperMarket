using SupermarketSystem.Domain.Common;

namespace SupermarketSystem.Domain.Customers;

/// <summary>
/// Aggregate root. Global (customers can shop at any branch), soft-
/// deletable master data. CustomerPurchaseHistory was deliberately removed
/// from the model (Architecture Review §6/§31) — it's a query over
/// SaleInvoice/SaleInvoiceItem filtered by CustomerId, not a stored entity.
/// </summary>
public class Customer : AuditableEntity, ISoftDeletable, IHasRowVersion
{
    public string FullName { get; private set; } = null!;
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public bool IsDeleted { get; private set; }

    /// <summary>حظر يمنع تقديم طلبات جديدة عبر تطبيق الزبائن - لا يمنع بيع POS عادي له، ولا يحذف تاريخه. راجع نقاش "حظر رقم متلاعب".</summary>
    public bool IsBlocked { get; private set; }

    public byte[]? RowVersion { get; private set; }

    private readonly List<CustomerNote> _notes = new();
    public IReadOnlyCollection<CustomerNote> Notes => _notes.AsReadOnly();

    private Customer() { } // EF Core

    public Customer(string fullName, string? phone, string? email)
    {
        FullName = fullName;
        Phone = phone;
        Email = email;
    }

    public CustomerNote AddNote(string text)
    {
        var note = new CustomerNote(Id, text);
        _notes.Add(note);
        return note;
    }

    public void MarkDeleted() => IsDeleted = true;
    public void Restore() => IsDeleted = false;

    public void Block() => IsBlocked = true;
    public void Unblock() => IsBlocked = false;
}

public class CustomerNote : Entity
{
    public Guid CustomerId { get; private set; }
    public string Text { get; private set; } = null!;

    private CustomerNote() { } // EF Core

    internal CustomerNote(Guid customerId, string text)
    {
        CustomerId = customerId;
        Text = text;
    }
}
