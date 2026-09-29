using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SupermarketSystem.Domain.Branches;
using SupermarketSystem.Domain.Identity;
using SupermarketSystem.Domain.Partners;

namespace SupermarketSystem.Infrastructure.Persistence.Configurations;

public class PartnerConfiguration : IEntityTypeConfiguration<Partner>
{
    public void Configure(EntityTypeBuilder<Partner> builder)
    {
        builder.ToTable("Partners");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.FullName).IsRequired().HasMaxLength(Partner.MaxNameLength);
        builder.Property(p => p.Type).HasConversion<int>().IsRequired();
        builder.Property(p => p.SpeculativeProfitPercent).HasColumnType("decimal(9,4)");
        builder.Property(p => p.Notes).HasMaxLength(Partner.MaxNotesLength);
        builder.Property(p => p.TelegramPhone).HasMaxLength(Partner.MaxTelegramPhoneLength);
        builder.Property(p => p.CashierBarcodeHash).HasMaxLength(100);
        builder.Property(p => p.CashierBarcodeIssuedAtUtc).HasColumnType("datetime2");
        builder.Property(p => p.CreatedAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(p => p.UpdatedAtUtc).HasColumnType("datetime2");

        builder.HasIndex(p => new { p.BranchId, p.IsActive });
        builder.HasIndex(p => new { p.BranchId, p.UserId });

        builder.HasOne<Branch>().WithMany().HasForeignKey(p => p.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class PartnerOtpChallengeConfiguration : IEntityTypeConfiguration<PartnerOtpChallenge>
{
    public void Configure(EntityTypeBuilder<PartnerOtpChallenge> builder)
    {
        builder.ToTable("PartnerOtpChallenges");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.CodeHash).IsRequired().HasMaxLength(100);
        builder.Property(c => c.Amount).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(c => c.CreatedAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(c => c.ExpiresAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(c => c.ConsumedAtUtc).HasColumnType("datetime2");

        builder.HasIndex(c => new { c.PartnerId, c.CreatedAtUtc });

        builder.HasOne<Partner>().WithMany().HasForeignKey(c => c.PartnerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Branch>().WithMany().HasForeignKey(c => c.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(c => c.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class PartnerWithdrawalConfiguration : IEntityTypeConfiguration<PartnerWithdrawal>
{
    public void Configure(EntityTypeBuilder<PartnerWithdrawal> builder)
    {
        builder.ToTable("PartnerWithdrawals");
        builder.HasKey(w => w.Id);

        builder.Property(w => w.Amount).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(w => w.Source).HasConversion<int>().IsRequired();
        builder.Property(w => w.OccurredAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(w => w.Notes).HasMaxLength(PartnerWithdrawal.MaxNotesLength);

        builder.HasIndex(w => w.ClientRequestId).IsUnique();
        builder.HasIndex(w => new { w.PartnerId, w.OccurredAtUtc });
        builder.HasIndex(w => new { w.BranchId, w.OccurredAtUtc });

        builder.HasOne<Partner>().WithMany().HasForeignKey(w => w.PartnerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Branch>().WithMany().HasForeignKey(w => w.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(w => w.RecordedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(w => w.PaidByUserId).OnDelete(DeleteBehavior.Restrict);
        // سجل تاريخي بحت - تصحيح غلط = حركة معاكسة، مش تعديل.
    }
}

public class PartnerMonthlyStatementConfiguration : IEntityTypeConfiguration<PartnerMonthlyStatement>
{
    public void Configure(EntityTypeBuilder<PartnerMonthlyStatement> builder)
    {
        builder.ToTable("PartnerMonthlyStatements");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.NetProfit).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(s => s.UnallocatedAmount).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(s => s.GeneratedAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(s => s.CreatedAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(s => s.UpdatedAtUtc).HasColumnType("datetime2");

        // كشف واحد بس لكل (فرع، شهر) - التوليد التلقائي والإعادة اليدوية ما بيعملوا نسختين.
        builder.HasIndex(s => new { s.BranchId, s.Year, s.Month }).IsUnique();

        builder.HasOne<Branch>().WithMany().HasForeignKey(s => s.BranchId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(s => s.Lines).WithOne().HasForeignKey(l => l.StatementId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(s => s.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class PartnerStatementLineConfiguration : IEntityTypeConfiguration<PartnerStatementLine>
{
    public void Configure(EntityTypeBuilder<PartnerStatementLine> builder)
    {
        builder.ToTable("PartnerStatementLines");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.PartnerType).HasConversion<int>().IsRequired();
        builder.Property(l => l.CapitalBalance).HasColumnType("decimal(18,4)");
        builder.Property(l => l.SharePercent).HasColumnType("decimal(9,4)").IsRequired();
        builder.Property(l => l.ShareAmount).HasColumnType("decimal(18,4)").IsRequired();

        builder.HasIndex(l => l.PartnerId);
        builder.HasOne<Partner>().WithMany().HasForeignKey(l => l.PartnerId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class OwnerReceivableEntryConfiguration : IEntityTypeConfiguration<OwnerReceivableEntry>
{
    public void Configure(EntityTypeBuilder<OwnerReceivableEntry> builder)
    {
        builder.ToTable("OwnerReceivableEntries");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Type).HasConversion<int>().IsRequired();
        builder.Property(e => e.Amount).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(e => e.OccurredAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(e => e.RepaymentSource).HasConversion<int?>();
        builder.Property(e => e.Notes).HasMaxLength(OwnerReceivableEntry.MaxNotesLength);

        builder.HasIndex(e => new { e.BranchId, e.OwnerUserId });
        builder.HasIndex(e => e.ClientRequestId).IsUnique().HasFilter("[ClientRequestId] IS NOT NULL");

        builder.HasOne<Branch>().WithMany().HasForeignKey(e => e.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.RecordedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PartnerWithdrawal>().WithMany().HasForeignKey(e => e.PartnerWithdrawalId).OnDelete(DeleteBehavior.Restrict);
    }
}
