using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SupermarketSystem.Domain.Audit;

namespace SupermarketSystem.Infrastructure.Persistence.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.EntityType).IsRequired().HasMaxLength(200);
        builder.Property(a => a.Action).HasConversion<int>().IsRequired();
        // OldValues/NewValues are serialized JSON snapshots (see
        // AuditableEntitySaveChangesInterceptor) — nvarchar(max), no
        // meaningful max length to enforce.
        builder.Property(a => a.OldValues).HasColumnType("nvarchar(max)");
        builder.Property(a => a.NewValues).HasColumnType("nvarchar(max)");
        builder.Property(a => a.IpAddress).HasMaxLength(64);
        builder.Property(a => a.OccurredAtUtc).HasColumnType("datetime2").IsRequired();

        // The two realistic access patterns: "history of this record" and
        // "recent activity at this branch" — not indexing every column
        // (Architecture Review §13).
        builder.HasIndex(a => new { a.EntityType, a.EntityId });
        builder.HasIndex(a => new { a.BranchId, a.OccurredAtUtc });
        builder.HasIndex(a => a.UserId);

        // Deliberately NO foreign keys out of AuditLog to User/Branch/
        // anything else. EntityType+EntityId is a loose reference by
        // design (Domain remarks) and UserId/BranchId are nullable,
        // unenforced references for the same reason: AuditLog must never
        // be affected by a cascade path from business-data deletion, and
        // giving it real FKs would create exactly that risk.
    }
}
