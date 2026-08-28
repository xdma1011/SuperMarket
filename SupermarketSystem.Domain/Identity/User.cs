using SupermarketSystem.Domain.Common;

namespace SupermarketSystem.Domain.Identity;

/// <summary>
/// Aggregate root. Global — a user is not owned by a single branch; which
/// branches they can work at is UserBranch (a separate, independent join).
/// Soft-deletable master data (per Architecture Review §6): users are
/// deactivated/soft-deleted, never hard-deleted, because historical
/// transactional rows (SaleInvoice.CreatedByUserId etc.) Restrict-reference
/// them.
///
/// Auth is explicitly out of scope for Phase C, but the shape is ready for
/// it: PasswordHash is nullable and unused until Phase 2 wires up real
/// authentication.
/// </summary>
public class User : AuditableEntity, ISoftDeletable, IHasRowVersion
{
    /// <summary>
    /// Well-known id for a seeded "System" user row. TEMPORARY, pending real
    /// authentication (Architecture Review §22 / Phase D "D11").
    ///
    /// Some entities require a real, non-nullable actor by design — e.g.
    /// StockMovement.UserId, deliberately NOT nullable, because "who moved
    /// this stock" is core to the ledger's audit value, not optional
    /// metadata. Until PlaceholderCurrentUserContext is replaced by real
    /// authentication, ICurrentUserContext.UserId is always null, so those
    /// entities cannot be constructed from the current authenticated user —
    /// there isn't one. Application handlers that hit this fall back to
    /// `currentUser.UserId ?? User.SystemUserId`, explicitly and visibly,
    /// rather than silently relaxing the Domain constraint to nullable.
    ///
    /// Every stock movement recorded under this id is honestly attributed —
    /// it says "no real user was authenticated when this happened," which
    /// remains true and auditable after real authentication ships (existing
    /// history is not retroactively misattributed to a real person who
    /// didn't act).
    /// </summary>
    public static readonly Guid SystemUserId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public string FullName { get; private set; } = null!;
    public string Username { get; private set; } = null!;
    public string Email { get; private set; } = null!;
    public string? PasswordHash { get; private set; }
    public bool IsActive { get; private set; }
    public bool IsDeleted { get; private set; }
    public byte[]? RowVersion { get; private set; }

    private User() { } // EF Core

    public User(string fullName, string username, string email)
    {
        FullName = fullName;
        Username = username;
        Email = email;
        IsActive = true;
    }

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
    public void MarkDeleted() => IsDeleted = true;
    public void Restore() => IsDeleted = false;

    public void UpdateProfile(string fullName, string email)
    {
        FullName = fullName;
        Email = email;
    }

    /// <summary>
    /// يستقبل بصمة *مُجزَّأة مسبقًا* لا كلمة سر خام — التجزئة نفسها تصير
    /// بطبقة Infrastructure (IPasswordHasher)، لأنها خوارزمية بتتغيّر مع
    /// توصيات الأمان، والـDomain ما بيعرف عنها شيئًا.
    ///
    /// التسمية صريحة (SetPasswordHash لا SetPassword) عشان يستحيل على أي
    /// مستدعٍ يمرّر كلمة سر خام بالغلط ويخزّنها بلا تجزئة — وهو خطأ ممكن
    /// جدًا لو كان الاسم غامضًا، ونتيجته كارثية وصامتة.
    /// </summary>
    public void SetPasswordHash(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new DomainException("A password hash is required.");
        }

        PasswordHash = passwordHash;
    }
}

/// <summary>
/// User-owned. A known device/session endpoint for the user — supports
/// future refresh-token/session management. Independent entity, not loaded
/// as part of the User aggregate (keeps User lean).
/// </summary>
public class UserDevice : AuditableEntity
{
    public Guid UserId { get; private set; }
    public string DeviceIdentifier { get; private set; } = null!;
    public string? DeviceName { get; private set; }
    public DateTime LastSeenAtUtc { get; private set; }
    public bool IsTrusted { get; private set; }

    private UserDevice() { } // EF Core

    public UserDevice(Guid userId, string deviceIdentifier, string? deviceName, DateTime lastSeenAtUtc)
    {
        UserId = userId;
        DeviceIdentifier = deviceIdentifier;
        DeviceName = deviceName;
        LastSeenAtUtc = lastSeenAtUtc;
        IsTrusted = false;
    }

    public void Trust() => IsTrusted = true;
    public void Touch(DateTime utcNow) => LastSeenAtUtc = utcNow;
}

/// <summary>
/// User-owned, historical/append-only. Never updated after creation.
/// BranchId is nullable — a login attempt may happen before a branch
/// context is selected, or the concept may not apply to every login surface.
/// </summary>
public class UserLoginLog : Entity
{
    public Guid UserId { get; private set; }
    public Guid? BranchId { get; private set; }
    public DateTime AttemptedAtUtc { get; private set; }
    public bool Success { get; private set; }
    public string? IpAddress { get; private set; }

    private UserLoginLog() { } // EF Core

    public UserLoginLog(Guid userId, Guid? branchId, DateTime attemptedAtUtc, bool success, string? ipAddress)
    {
        UserId = userId;
        BranchId = branchId;
        AttemptedAtUtc = attemptedAtUtc;
        Success = success;
        IpAddress = ipAddress;
    }
}
