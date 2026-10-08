using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SupermarketSystem.Domain.Branches;
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
        builder.Property(c => c.ShiftNumber).IsRequired();
        builder.Property(c => c.ClosedAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(c => c.ExpectedCash).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(c => c.CountedCash).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(c => c.RowVersion).IsRowVersion();
        builder.Property(c => c.CreatedAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(c => c.UpdatedAtUtc).HasColumnType("datetime2");

        builder.Ignore(c => c.Variance);
        builder.Property(c => c.PendingSalesCount).HasDefaultValue(0);
        builder.Property(c => c.PendingSalesAmount).HasColumnType("decimal(18,4)").HasDefaultValue(0m);

        // One closing per branch per business day PER SHIFT — enforced
        // against (BranchId, BusinessDate, ShiftNumber), not ClosedAtUtc
        // (an exact timestamp can never collide, so a unique index on it
        // enforces nothing). كان (BranchId, BusinessDate) بس قبل 21/9/2026 -
        // وسّعناه بطلب صاحب المشروع الصريح لدعم تسليم/استلام صندوق منفصل
        // بين وردية صباح ومساء بنفس اليوم.
        builder.HasIndex(c => new { c.BranchId, c.BusinessDate, c.ShiftNumber }).IsUnique();

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

public class DrawerOpenEventConfiguration : IEntityTypeConfiguration<DrawerOpenEvent>
{
    public void Configure(EntityTypeBuilder<DrawerOpenEvent> builder)
    {
        builder.ToTable("DrawerOpenEvents");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.OccurredAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(e => e.Reason).HasMaxLength(DrawerOpenEvent.MaxReasonLength);

        builder.HasIndex(e => new { e.BranchId, e.OccurredAtUtc });
        builder.Property(e => e.RecordedAtUtc).HasColumnType("datetime2");
        // Idempotency للفتحات المحفوظة أوفلاين (نفس فلسفة SaleInvoice.ClientRequestId) - السجلات القديمة null.
        builder.HasIndex(e => e.ClientRequestId).IsUnique().HasFilter("[ClientRequestId] IS NOT NULL");

        builder.HasOne<User>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Branch>().WithMany().HasForeignKey(e => e.BranchId).OnDelete(DeleteBehavior.Restrict);
        // سجل تاريخي بحت - ما في أي مسار تعديل/حذف بالنموذج.
    }
}

public class CashClosingVarianceNoteConfiguration : IEntityTypeConfiguration<CashClosingVarianceNote>
{
    public void Configure(EntityTypeBuilder<CashClosingVarianceNote> builder)
    {
        builder.ToTable("CashClosingVarianceNotes");
        builder.HasKey(n => n.Id);

        builder.Property(n => n.Reason).HasConversion<int>().IsRequired();
        builder.Property(n => n.ExplainedAmount).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(n => n.Note).HasMaxLength(CashClosingVarianceNote.MaxNoteLength);
        builder.Property(n => n.RecordedAtUtc).HasColumnType("datetime2").IsRequired();

        builder.HasIndex(n => n.CashClosingId);

        builder.HasOne<CashClosing>().WithMany().HasForeignKey(n => n.CashClosingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(n => n.RecordedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Branch>().WithMany().HasForeignKey(n => n.BranchId).OnDelete(DeleteBehavior.Restrict);
        // سجل تاريخي بحت - ما في مسار تعديل/حذف.
    }
}
