using SupermarketSystem.Domain.Common;

namespace SupermarketSystem.Domain.Purchasing;

/// <summary>
/// Aggregate root. Global master data (Architecture Review assumption:
/// suppliers are company-wide, not branch-specific — flagged as an
/// assumption to confirm; the same BranchPaymentMethod-style junction
/// pattern would apply if branch-specific suppliers are ever needed).
/// </summary>
public class Supplier : AuditableEntity, ISoftDeletable, IHasRowVersion
{
    public string Name { get; private set; } = null!;
    public string? ContactName { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public Address? Address { get; private set; }
    public bool IsActive { get; private set; }
    public bool IsDeleted { get; private set; }
    public byte[]? RowVersion { get; private set; }

    private Supplier() { } // EF Core

    public Supplier(string name, string? contactName, string? phone, string? email, Address? address)
    {
        Name = name;
        ContactName = contactName;
        Phone = phone;
        Email = email;
        Address = address;
        IsActive = true;
    }

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
    public void MarkDeleted() => IsDeleted = true;
    public void Restore() => IsDeleted = false;

    public void UpdateDetails(string name, string? contactName, string? phone, string? email)
    {
        Name = name;
        ContactName = contactName;
        Phone = phone;
        Email = email;
    }
}
