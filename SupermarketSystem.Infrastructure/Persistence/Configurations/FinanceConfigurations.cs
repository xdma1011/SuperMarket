using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SupermarketSystem.Domain.Finance;
using SupermarketSystem.Domain.Identity;

namespace SupermarketSystem.Infrastructure.Persistence.Configurations;

public class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> builder)
    {
        builder.ToTable("Expenses");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Category).HasConversion<int>().IsRequired();
        builder.Property(e => e.Amount).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(e => e.PaymentDateUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(e => e.Notes).HasMaxLength(1000);

        // الاستعلام الحرج بـGetMonthlyProfitStatement: فرع + فترة (لا تاريخ
        // الدفع الفعلي) - راجع تعليق PeriodYear/PeriodMonth بالـDomain.
        builder.HasIndex(e => new { e.BranchId, e.PeriodYear, e.PeriodMonth });

        builder.HasOne<User>().WithMany().HasForeignKey(e => e.RecordedByUserId).OnDelete(DeleteBehavior.Restrict);
        // Branch FK (Restrict) configured on the Branches side.
        // No update path is exposed anywhere in the model for this entity —
        // append-only by construction (نفس فلسفة CashDrawerLog).
    }
}

public class CapitalTransactionConfiguration : IEntityTypeConfiguration<CapitalTransaction>
{
    public void Configure(EntityTypeBuilder<CapitalTransaction> builder)
    {
        builder.ToTable("CapitalTransactions");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Type).HasConversion<int>().IsRequired();
        builder.Property(c => c.Amount).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(c => c.OccurredAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(c => c.Notes).HasMaxLength(1000);

        builder.HasIndex(c => new { c.BranchId, c.OccurredAtUtc });

        builder.HasOne<User>().WithMany().HasForeignKey(c => c.RecordedByUserId).OnDelete(DeleteBehavior.Restrict);
        // Branch FK (Restrict) configured on the Branches side.
        // No update path is exposed anywhere in the model for this entity —
        // append-only by construction (نفس فلسفة CashDrawerLog).
    }
}
