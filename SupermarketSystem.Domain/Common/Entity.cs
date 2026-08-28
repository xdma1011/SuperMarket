namespace SupermarketSystem.Domain.Common;

/// <summary>
/// Base type for every entity in the model. Identity only — no audit/branch/
/// concurrency baggage forced onto entities that don't need it (see the
/// opt-in interfaces below). Guid keys are used throughout so aggregate
/// graphs (e.g. a SaleInvoice with its Items/Payments) can be fully
/// constructed in memory, with every child already knowing its parent's
/// id, before a single round-trip to the database.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; protected set; }

    protected Entity()
    {
        Id = Guid.NewGuid();
    }

    protected Entity(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Entity id cannot be empty.", nameof(id));
        }

        Id = id;
    }

    public override bool Equals(object? obj)
    {
        if (obj is not Entity other || other.GetType() != GetType())
        {
            return false;
        }

        return Id == other.Id;
    }

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);
}

/// <summary>
/// Opt-in: entities that carry creation/modification audit metadata.
/// Not every entity needs both halves — e.g. an append-only ledger row
/// only ever has a "created" side — but the interface is uniform so the
/// SaveChanges audit interceptor (Infrastructure) can populate it
/// generically via reflection without per-entity wiring.
/// </summary>
public interface IAuditable
{
    DateTime CreatedAtUtc { get; }
    Guid? CreatedByUserId { get; }
    DateTime? UpdatedAtUtc { get; }
    Guid? UpdatedByUserId { get; }

    void SetCreationAudit(DateTime utcNow, Guid? userId);
    void SetModificationAudit(DateTime utcNow, Guid? userId);
}

/// <summary>
/// Opt-in: entities scoped to a single branch. Implementing this is what
/// makes an entity eligible for the reflection-driven global query filter
/// in AppDbContext (see Architecture Review §9 "Multi-Branch Strategy") —
/// nobody has to remember to filter branch-owned entities by hand.
/// </summary>
public interface IBranchOwned
{
    Guid BranchId { get; }
}

/// <summary>
/// Opt-in: entities that support soft delete (master/reference data only —
/// never applied to transactional or historical entities per the
/// Architecture Review's explicit soft-delete policy).
/// </summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; }
    void MarkDeleted();
    void Restore();
}

/// <summary>
/// Opt-in: entities under real write contention that need SQL Server
/// ROWVERSION-based optimistic concurrency.
/// </summary>
public interface IHasRowVersion
{
    byte[]? RowVersion { get; }
}

/// <summary>
/// Base for entities carrying full audit metadata. Setters are internal to
/// the assembly-visible contract (via the IAuditable methods) rather than
/// public — only the persistence interceptor is expected to call these,
/// not business/application code.
/// </summary>
public abstract class AuditableEntity : Entity, IAuditable
{
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedByUserId { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }
    public Guid? UpdatedByUserId { get; private set; }

    protected AuditableEntity() : base() { }
    protected AuditableEntity(Guid id) : base(id) { }

    public void SetCreationAudit(DateTime utcNow, Guid? userId)
    {
        CreatedAtUtc = utcNow;
        CreatedByUserId = userId;
    }

    public void SetModificationAudit(DateTime utcNow, Guid? userId)
    {
        UpdatedAtUtc = utcNow;
        UpdatedByUserId = userId;
    }
}

/// <summary>
/// Thrown for domain invariant violations raised from within entity/aggregate
/// methods (e.g. attempting to void a sale that already has returns). Distinct
/// from validation errors, which belong to the Application layer.
/// </summary>
public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}
