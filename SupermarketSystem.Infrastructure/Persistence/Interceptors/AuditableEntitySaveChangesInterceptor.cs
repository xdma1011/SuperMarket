using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Domain.Audit;
using SupermarketSystem.Domain.Common;

namespace SupermarketSystem.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Does two things at SaveChanges time, both of which would otherwise have
/// to be remembered by hand in every command handler:
///
///   1. Stamps CreatedAtUtc/CreatedByUserId and UpdatedAtUtc/UpdatedByUserId
///      on any IAuditable entity being added/modified.
///   2. Writes AuditLog rows capturing who/what/when/branch/old/new for
///      tracked business changes.
///
/// Why an interceptor rather than domain code: this keeps auditing entirely
/// out of the Domain layer (Architecture Review — "design automatic audit
/// cleanly without unnecessary coupling to Domain"). The Domain entities
/// know nothing about AuditLog; they only expose the IAuditable methods.
///
/// AUDIT SCOPE — deliberately not "every entity". The Architecture Review's
/// rule is "audit important business and security changes, not every
/// database read/write blindly". Two categories are excluded:
///   (a) Append-only ledgers (StockMovement, CashDrawerLog, UserLoginLog,
///       AuditLog itself) — they ARE the historical record, so auditing
///       their insertion would duplicate data one-for-one with no added
///       information.
///   (b) Child/line entities of an aggregate that is itself audited
///       (SaleInvoiceItem, PurchaseInvoiceItem, ReturnInvoiceItem,
///       SuspendedSaleItem, StocktakeItem, CashClosingDetail, ProductUnit,
///       ProductBarcode, ProductImage, ProductNote, CustomerNote,
///       RolePermission, UserRole, UserBranch) — their creation is already
///       implied by the parent's own Created row; auditing each one
///       individually multiplies AuditLog volume for no material benefit.
/// Audited: SaleInvoice/ReturnInvoice/PurchaseInvoice lifecycle, Payment/
/// Reversal, ProductBranch price/availability, Stock, CashClosing,
/// User/Role/Permission, and every other aggregate root not listed above.
/// Which types are excluded is controlled by the set below, kept in one
/// place rather than
/// scattered as per-entity attributes.
/// </summary>
public class AuditableEntitySaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IDateTimeProvider _dateTimeProvider;

    private static readonly HashSet<Type> AuditExcludedTypes = new()
    {
        // Append-only ledgers — the ledger row itself IS the historical
        // record; a duplicate AuditLog entry adds no information.
        typeof(AuditLog),
        typeof(Domain.Inventory.StockMovement),
        typeof(Domain.CashManagement.CashDrawerLog),
        typeof(Domain.Identity.UserLoginLog),
        typeof(Domain.Notifications.NotificationLog),
        typeof(BranchDocumentSequence),

        // Child/line entities of an already-audited aggregate root. Their
        // creation is implied by the parent's own Created audit row
        // (SaleInvoice, PurchaseInvoice, ReturnInvoice, SuspendedSale,
        // Stocktake, CashClosing, Product, Customer, Role); auditing every
        // line item/child individually would multiply AuditLog volume
        // (e.g. one 10-item sale -> 10+ extra rows) for no material
        // security or business value.
        typeof(Domain.Sales.SaleInvoiceItem),
        typeof(Domain.Purchasing.PurchaseInvoiceItem),
        typeof(Domain.Sales.ReturnInvoiceItem),
        typeof(Domain.Sales.SuspendedSaleItem),
        typeof(Domain.Inventory.StocktakeItem),
        typeof(Domain.CashManagement.CashClosingDetail),
        typeof(Domain.Catalog.ProductUnit),
        typeof(Domain.Catalog.ProductBarcode),
        typeof(Domain.Catalog.ProductImage),
        typeof(Domain.Catalog.ProductNote),
        typeof(Domain.Customers.CustomerNote),
        typeof(Domain.Identity.RolePermission),
        typeof(Domain.Identity.UserRole),
        typeof(Domain.Identity.UserBranch)
    };

    public AuditableEntitySaveChangesInterceptor(
        ICurrentUserContext currentUserContext,
        IDateTimeProvider dateTimeProvider)
    {
        _currentUserContext = currentUserContext;
        _dateTimeProvider = dateTimeProvider;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var utcNow = _dateTimeProvider.UtcNow;
        var userId = _currentUserContext.UserId;
        var branchId = _currentUserContext.BranchId;

        StampAuditFields(context, utcNow, userId);

        // Collected first, then added — adding to the change tracker while
        // enumerating Entries() would invalidate the enumeration.
        var auditEntries = BuildAuditEntries(context, utcNow, userId, branchId);
        foreach (var auditEntry in auditEntries)
        {
            context.Add(auditEntry);
        }
    }

    private static void StampAuditFields(DbContext context, DateTime utcNow, Guid? userId)
    {
        foreach (var entry in context.ChangeTracker.Entries<IAuditable>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.SetCreationAudit(utcNow, userId);
                    break;
                case EntityState.Modified:
                    entry.Entity.SetModificationAudit(utcNow, userId);
                    break;
            }
        }
    }

    private static List<AuditLog> BuildAuditEntries(DbContext context, DateTime utcNow, Guid? userId, Guid? branchId)
    {
        var auditEntries = new List<AuditLog>();

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Entity is not Entity domainEntity)
            {
                continue;
            }

            var clrType = entry.Entity.GetType();
            if (AuditExcludedTypes.Contains(clrType))
            {
                continue;
            }

            var action = entry.State switch
            {
                EntityState.Added => AuditAction.Created,
                EntityState.Modified => ResolveModifiedAction(entry),
                EntityState.Deleted => AuditAction.Deleted,
                _ => (AuditAction?)null
            };

            if (action is null)
            {
                continue;
            }

            var (oldValues, newValues) = CaptureValues(entry);

            // Prefer the entity's own BranchId over the ambient context —
            // a cross-branch administrative operation should be audited
            // against the branch actually affected, not the operator's.
            var effectiveBranchId = entry.Entity is IBranchOwned branchOwned
                ? branchOwned.BranchId
                : branchId;

            auditEntries.Add(new AuditLog(
                userId,
                effectiveBranchId,
                clrType.Name,
                domainEntity.Id,
                action.Value,
                oldValues,
                newValues,
                correlationId: null,
                ipAddress: null,
                occurredAtUtc: utcNow));
        }

        return auditEntries;
    }

    /// <summary>
    /// A soft delete is a Modified entry at the EF level, but semantically
    /// it's a delete (or a restore) — recording it as a generic "Updated"
    /// would bury the single most security-relevant change a master-data
    /// row can undergo.
    /// </summary>
    private static AuditAction ResolveModifiedAction(EntityEntry entry)
    {
        if (entry.Entity is not ISoftDeletable)
        {
            return AuditAction.Updated;
        }

        var isDeletedProperty = entry.Properties.FirstOrDefault(p => p.Metadata.Name == nameof(ISoftDeletable.IsDeleted));
        if (isDeletedProperty is null || !isDeletedProperty.IsModified)
        {
            return AuditAction.Updated;
        }

        return isDeletedProperty.CurrentValue is true ? AuditAction.Deleted : AuditAction.Restored;
    }

    private static (string? OldValues, string? NewValues) CaptureValues(EntityEntry entry)
    {
        var oldValues = new Dictionary<string, object?>();
        var newValues = new Dictionary<string, object?>();

        foreach (var property in entry.Properties)
        {
            // RowVersion changes on every write and carries no business
            // meaning — recording it would add noise to every audit row.
            if (property.Metadata.Name == nameof(IHasRowVersion.RowVersion))
            {
                continue;
            }

            switch (entry.State)
            {
                case EntityState.Added:
                    newValues[property.Metadata.Name] = property.CurrentValue;
                    break;

                case EntityState.Deleted:
                    oldValues[property.Metadata.Name] = property.OriginalValue;
                    break;

                case EntityState.Modified when property.IsModified:
                    oldValues[property.Metadata.Name] = property.OriginalValue;
                    newValues[property.Metadata.Name] = property.CurrentValue;
                    break;
            }
        }

        return (
            oldValues.Count > 0 ? JsonSerializer.Serialize(oldValues) : null,
            newValues.Count > 0 ? JsonSerializer.Serialize(newValues) : null);
    }
}
