using SupermarketSystem.Domain.Common;

namespace SupermarketSystem.Application.Common.Interfaces;

/// <summary>
/// The "who is asking, and at which branch" contract used by:
///   - AppDbContext's global query filter (Architecture Review §9), and
///   - the SaveChanges audit interceptor, to populate CreatedByUserId /
///     UpdatedByUserId and AuditLog.UserId/BranchId.
///
/// Authentication is explicitly out of scope for Phase C. Infrastructure
/// provides a placeholder implementation (returns nulls / IsCrossBranchAccessAllowed
/// = true) until Phase 2 wires this up to real authentication — see
/// Infrastructure/Services/NullCurrentUserContext.cs. Application-layer
/// authorization checks (branch-access validation, independent of the DB
/// query filter — §9 "also enforced at the application level") will read
/// this interface once real command/query handlers exist.
/// </summary>
public interface ICurrentUserContext
{
    Guid? UserId { get; }
    Guid? BranchId { get; }
    bool IsCrossBranchAccessAllowed { get; }

    /// <summary>عنوان IP الفعلي لصاحب الطلب — لسجلات المراجعة (AuditLog). Null لطلب بلا HttpContext (اختبارات، خدمات خلفية).</summary>
    string? IpAddress { get; }

    /// <summary>معرّف واحد ثابت لكل طلب HTTP — يربط كل سطور AuditLog الناتجة عن نفس العملية ببعضها.</summary>
    Guid? CorrelationId { get; }
}

/// <summary>Trivial UTC clock abstraction — testability, and a single place enforcing "persisted timestamps are UTC" (Architecture Review §5).</summary>
public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
}

/// <summary>
/// Contract for the atomic per-branch invoice numbering design
/// (Architecture Review §4). The Infrastructure implementation issues the
/// raw `UPDATE ... OUTPUT` reservation against BranchDocumentSequence —
/// never MAX+1, never a global IDENTITY.
/// </summary>
public interface IDocumentNumberGenerator
{
    Task<string> GetNextNumberAsync(Guid branchId, DocumentType documentType, CancellationToken cancellationToken);
}
