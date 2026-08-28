using SupermarketSystem.Domain.Common;

namespace SupermarketSystem.Domain.Audit;

public enum AuditAction
{
    Created = 1,
    Updated = 2,
    Deleted = 3,
    Restored = 4
}

/// <summary>
/// Aggregate root, append-only, historical. Generic entity-change audit
/// trail — folds in what would otherwise have been four separate,
/// near-identical tables (PriceChangeLog, StockAdjustmentLog,
/// PermissionChangeLog, SettingChangeLog — all removed, Architecture Review
/// §4). EntityType/EntityId is a loose reference (no DB FK, same trade-off
/// as StockMovement.ReferenceType — see §8) since a hard FK to "any entity
/// in the system" isn't expressible as a single constraint and would
/// require the Audit module to know every other module's PK/table, which
/// would be a circular-dependency risk (Phase B).
/// Never cascade-deleted by any business-data deletion — nothing in the
/// model has a cascade path pointing at AuditLog.
/// </summary>
public class AuditLog : Entity
{
    public Guid? UserId { get; private set; }
    public Guid? BranchId { get; private set; }
    public string EntityType { get; private set; } = null!;
    public Guid EntityId { get; private set; }
    public AuditAction Action { get; private set; }
    public string? OldValues { get; private set; }
    public string? NewValues { get; private set; }
    public Guid? CorrelationId { get; private set; }
    public string? IpAddress { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }

    private AuditLog() { } // EF Core

    public AuditLog(
        Guid? userId,
        Guid? branchId,
        string entityType,
        Guid entityId,
        AuditAction action,
        string? oldValues,
        string? newValues,
        Guid? correlationId,
        string? ipAddress,
        DateTime occurredAtUtc)
    {
        UserId = userId;
        BranchId = branchId;
        EntityType = entityType;
        EntityId = entityId;
        Action = action;
        OldValues = oldValues;
        NewValues = newValues;
        CorrelationId = correlationId;
        IpAddress = ipAddress;
        OccurredAtUtc = occurredAtUtc;
    }
}
