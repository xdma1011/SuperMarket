using SupermarketSystem.Domain.Common;

namespace SupermarketSystem.Domain.Identity;

/// <summary>
/// Join entity, User &lt;-&gt; Branch. Which branches a user can access.
/// Added in Architecture Review §4 (was missing from the original entity
/// list) — needed as soon as more than one branch exists and staff work at
/// a subset of them. This is what Application-layer branch-authorization
/// checks (§9 "also enforced at the application level") validate against,
/// independent of the DB-level global query filter.
/// </summary>
public class UserBranch : Entity
{
    public Guid UserId { get; private set; }
    public Guid BranchId { get; private set; }
    public bool IsDefault { get; private set; }

    private UserBranch() { } // EF Core

    public UserBranch(Guid userId, Guid branchId, bool isDefault = false)
    {
        UserId = userId;
        BranchId = branchId;
        IsDefault = isDefault;
    }

    public void SetAsDefault() => IsDefault = true;
}
