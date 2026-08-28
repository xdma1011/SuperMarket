using SupermarketSystem.Domain.Common;

namespace SupermarketSystem.Domain.Identity;

/// <summary>
/// Aggregate root. Global, admin-managed. Small aggregate together with
/// RolePermission (its own join children) — Permission itself is a separate
/// aggregate root (see below), referenced by id, not owned.
/// </summary>
public class Role : AuditableEntity, IHasRowVersion
{
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public bool IsActive { get; private set; }
    public byte[]? RowVersion { get; private set; }

    private readonly List<RolePermission> _permissions = new();
    public IReadOnlyCollection<RolePermission> Permissions => _permissions.AsReadOnly();

    private Role() { } // EF Core

    public Role(string name, string? description)
    {
        Name = name;
        Description = description;
        IsActive = true;
    }

    public void GrantPermission(Guid permissionId)
    {
        if (_permissions.Any(p => p.PermissionId == permissionId))
        {
            return;
        }

        _permissions.Add(new RolePermission(Id, permissionId));
    }

    public void RevokePermission(Guid permissionId)
    {
        _permissions.RemoveAll(p => p.PermissionId == permissionId);
    }
}

/// <summary>
/// Aggregate root, not an enum. Per Architecture Review §29/§7: permissions
/// are exactly the kind of "fixed value administrators may need to add
/// without a redeploy" case that rules out an enum. Global reference data.
/// </summary>
public class Permission : AuditableEntity
{
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }

    private Permission() { } // EF Core

    public Permission(string code, string name, string? description)
    {
        Code = code;
        Name = name;
        Description = description;
    }
}

/// <summary>
/// Join entity, User &lt;-&gt; Role. BranchId is nullable and optional:
/// null means the role assignment applies at every branch the user has
/// access to (via UserBranch); a populated value scopes the role to one
/// specific branch (Architecture Review §9 assumption — role assignment can
/// optionally be branch-scoped).
/// </summary>
public class UserRole : Entity
{
    public Guid UserId { get; private set; }
    public Guid RoleId { get; private set; }
    public Guid? BranchId { get; private set; }

    private UserRole() { } // EF Core

    public UserRole(Guid userId, Guid roleId, Guid? branchId = null)
    {
        UserId = userId;
        RoleId = roleId;
        BranchId = branchId;
    }
}

/// <summary>
/// Join entity, Role &lt;-&gt; Permission. Owned/managed within the Role
/// aggregate (see Role.GrantPermission/RevokePermission) — not constructed
/// directly from outside.
/// </summary>
public class RolePermission : Entity
{
    public Guid RoleId { get; private set; }
    public Guid PermissionId { get; private set; }

    private RolePermission() { } // EF Core

    internal RolePermission(Guid roleId, Guid permissionId)
    {
        RoleId = roleId;
        PermissionId = permissionId;
    }
}
