using SupermarketSystem.Domain.Common;

namespace SupermarketSystem.Domain.Payments;

/// <summary>
/// Shared by SaleInvoicePayment and ReturnInvoicePayment. Only three states
/// in Phase 1 — see Architecture Review §16.5 for why Pending/Cancelled were
/// deliberately excluded (no live gateway wait state; an abandoned attempt
/// is never persisted).
/// </summary>
public enum PaymentStatus
{
    Completed = 1,
    Failed = 2,
    Reversed = 3
}

/// <summary>
/// Aggregate root, global reference data. An entity, not an enum — the
/// entire point is that Mastercard/bank transfer/store credit/gift card can
/// be added from Settings/Admin without a code change or redeploy
/// (Architecture Review §16.1).
///
/// Code is treated as immutable after creation (a stable technical key);
/// Name is the editable/localizable display value. Never hard-deleted while
/// referenced by historical payments — deactivate via IsActive instead.
/// </summary>
public class PaymentMethod : AuditableEntity, IHasRowVersion
{
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public int SortOrder { get; private set; }

    /// <summary>
    /// Informational/protective only (e.g. an extra confirmation before
    /// deactivating a seeded method in the admin UI) — NOT a hard database
    /// or domain invariant. Every PaymentMethod, system-defined or not,
    /// follows the same real invariants: Code immutable, never hard-deleted
    /// while referenced. See Architecture Review §16.1.
    /// </summary>
    public bool IsSystemDefined { get; private set; }

    /// <summary>
    /// Whether recording a payment of this method produces a CashDrawerLog
    /// entry. Named after the behavior, not the identity — nothing in the
    /// system ever checks Code == "CASH" to decide drawer impact (§16.2).
    /// </summary>
    public bool AffectsCashDrawer { get; private set; }

    /// <summary>
    /// Whether an ExternalReference must be captured for a payment of this
    /// method. Enforced at the Application layer (a nullable column can't
    /// conditionally require itself at the DB level).
    /// </summary>
    public bool RequiresExternalReference { get; private set; }

    public byte[]? RowVersion { get; private set; }

    private PaymentMethod() { } // EF Core

    public PaymentMethod(
        string code,
        string name,
        bool affectsCashDrawer,
        bool requiresExternalReference,
        int sortOrder,
        bool isSystemDefined = false)
    {
        Code = code;
        Name = name;
        AffectsCashDrawer = affectsCashDrawer;
        RequiresExternalReference = requiresExternalReference;
        SortOrder = sortOrder;
        IsSystemDefined = isSystemDefined;
        IsActive = true;
    }

    public void Rename(string name) => Name = name;
    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
    public void Reorder(int sortOrder) => SortOrder = sortOrder;
}
