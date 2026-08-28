using SupermarketSystem.Domain.Common;

namespace SupermarketSystem.Domain.Sales;

public enum DiscountType
{
    Percentage = 1,
    FixedAmount = 2
}

/// <summary>
/// Aggregate root. Configurable, admin-managed discount/promotion rules
/// (e.g. "Senior citizen 10%"). Global by default with an optional branch
/// scope for branch-specific promotions. The VALUE actually applied to a
/// sale is always a snapshot on SaleInvoice/SaleInvoiceItem — this entity
/// is referenced only for traceability (nullable FK, SetNull on delete), so
/// deprecating a Discount rule never invalidates historical invoices
/// (Architecture Review §11/§13.6). A discount snapshot with no DiscountId
/// IS a manual/ad-hoc discount — that absence is how "manual discounts" are
/// distinguished for management-visibility queries, with no separate flag.
/// </summary>
public class Discount : AuditableEntity, IHasRowVersion
{
    public string Name { get; private set; } = null!;
    public DiscountType Type { get; private set; }
    public decimal Value { get; private set; }
    public Guid? BranchId { get; private set; }
    public bool IsActive { get; private set; }
    public byte[]? RowVersion { get; private set; }

    private Discount() { } // EF Core

    public Discount(string name, DiscountType type, decimal value, Guid? branchId)
    {
        Name = name;
        Type = type;
        Value = value;
        BranchId = branchId;
        IsActive = true;
    }

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
}
