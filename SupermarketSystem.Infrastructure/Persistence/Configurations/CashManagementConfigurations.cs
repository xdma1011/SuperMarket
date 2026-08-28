using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SupermarketSystem.Domain.CashManagement;
using SupermarketSystem.Domain.Identity;
using SupermarketSystem.Domain.Payments;

namespace SupermarketSystem.Infrastructure.Persistence.Configurations;

public class CashDrawerLogConfiguration : IEntityTypeConfiguration<CashDrawerLog>
{
    public void Configure(EntityTypeBuilder<CashDrawerLog> builder)
    {
        builder.ToTable("CashDrawerLogs");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.MovementType).HasConversion<int>().IsRequired();
        builder.Property(c => c.Amount).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(c => c.ReferenceType).HasConversion<int>().IsRequired();
        builder.Property(c => c.OccurredAtUtc).HasColumnType("datetime2").IsRequired();
        // ReferenceId is a deliberate loose reference — no FK (see class remarks in Domain).

        builder.HasIndex(c => new { c.BranchId, c.OccurredAtUtc });
        builder.HasIndex(c => new { c.ReferenceType, c.ReferenceId });

        builder.HasOne<User>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Restrict);
        // Branch FK (Restrict) configured on the Branches side.
        // No update path is exposed anywhere in the model for this entity —
        // append-only by construction, not just by convention.
    }
}

public class CashClosingConfiguration : IEntityTypeConfiguration<CashClosing>
{
    public void Configure(EntityTypeBuilder<CashClosing> builder)
    {
        builder.ToTable("CashClosings");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.BusinessDate).HasColumnType("date").IsRequired();
        builder.Property(c => c.ClosedAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(c => c.ExpectedCash).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(c => c.CountedCash).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(c => c.RowVersion).IsRowVersion();
        builder.Property(c => c.CreatedAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(c => c.UpdatedAtUtc).HasColumnType("datetime2");

        builder.Ignore(c => c.Variance);

        // One closing per branch per business day — enforced against the
        // explicit BusinessDate, not ClosedAtUtc (an exact timestamp can
        // never collide, so a unique index on it enforces nothing).
        builder.HasIndex(c => new { c.BranchId, c.BusinessDate }).IsUnique();

        builder.HasOne<User>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Restrict);
        // Branch FK (Restrict) configured on the Branches side.

        builder.HasMany(c => c.Details).WithOne().HasForeignKey(d => d.CashClosingId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(c => c.Details).HasField("_details").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class CashClosingDetailConfiguration : IEntityTypeConfiguration<CashClosingDetail>
{
    public void Configure(EntityTypeBuilder<CashClosingDetail> builder)
    {
        builder.ToTable("CashClosingDetails");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.ExpectedAmount).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(d => d.CountedAmount).HasColumnType("decimal(18,4)");

        builder.HasOne<PaymentMethod>().WithMany().HasForeignKey(d => d.PaymentMethodId).OnDelete(DeleteBehavior.Restrict);
    }
}
