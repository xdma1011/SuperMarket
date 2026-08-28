using SupermarketSystem.Domain.Common;

namespace SupermarketSystem.Domain.Branches;

/// <summary>
/// Aggregate root. The one entity everything else's "branch ownership" is
/// ultimately relative to. Never hard-deleted while any transactional data
/// references it (Restrict everywhere) — a branch that closes is
/// deactivated, not removed, so its history remains queryable.
/// </summary>
public class Branch : AuditableEntity, ISoftDeletable, IHasRowVersion
{
    public string Name { get; private set; } = null!;
    public string Code { get; private set; } = null!;
    public Address? Address { get; private set; }
    public string? PhoneNumber { get; private set; }
    public bool IsActive { get; private set; }
    public bool IsDeleted { get; private set; }
    public byte[]? RowVersion { get; private set; }

    private Branch() { } // EF Core

    public Branch(string name, string code, Address? address, string? phoneNumber)
    {
        Name = name;
        Code = code;
        Address = address;
        PhoneNumber = phoneNumber;
        IsActive = true;
    }

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
    public void MarkDeleted() => IsDeleted = true;
    public void Restore() => IsDeleted = false;
}
