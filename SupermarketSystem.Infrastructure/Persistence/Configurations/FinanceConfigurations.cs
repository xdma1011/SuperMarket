using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SupermarketSystem.Domain.Branches;
using SupermarketSystem.Domain.Employees;
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

        // 29/9/2026: نوع يعرّفه صاحب المحل + "من الصندوق" + ربط بصرف راتب موظف.
        builder.Property(e => e.PaidFromDrawer).HasDefaultValue(false);
        builder.HasIndex(e => e.ExpenseTypeId);
        builder.HasIndex(e => e.EmployeePaymentId).IsUnique().HasFilter("[EmployeePaymentId] IS NOT NULL");
        builder.HasOne<ExpenseType>().WithMany().HasForeignKey(e => e.ExpenseTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<EmployeePayment>().WithMany().HasForeignKey(e => e.EmployeePaymentId).OnDelete(DeleteBehavior.Restrict);
        // Branch FK (Restrict) configured on the Branches side.
        // No update path is exposed anywhere in the model for this entity —
        // append-only by construction (نفس فلسفة CashDrawerLog).
    }
}

public class ExpenseTypeConfiguration : IEntityTypeConfiguration<ExpenseType>
{
    public void Configure(EntityTypeBuilder<ExpenseType> builder)
    {
        builder.ToTable("ExpenseTypes");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name).IsRequired().HasMaxLength(ExpenseType.MaxNameLength);
        builder.Property(t => t.LegacyCategory).HasConversion<int?>();
        builder.HasIndex(t => t.Name).IsUnique();

        // الأنواع الجاهزة - صاحب المحل بيقدر يغيّر اسمها أو يوقفها، وبيضيف أنواعه.
        builder.HasData(
            new { Id = ExpenseType.RentId, Name = "إيجار", IsActive = true, SortOrder = 1, LegacyCategory = (ExpenseCategory?)ExpenseCategory.Rent },
            new { Id = ExpenseType.ElectricityId, Name = "كهرباء", IsActive = true, SortOrder = 2, LegacyCategory = (ExpenseCategory?)ExpenseCategory.Electricity },
            new { Id = ExpenseType.WaterId, Name = "ماء", IsActive = true, SortOrder = 3, LegacyCategory = (ExpenseCategory?)ExpenseCategory.Water },
            new { Id = ExpenseType.SalaryId, Name = "رواتب", IsActive = true, SortOrder = 4, LegacyCategory = (ExpenseCategory?)ExpenseCategory.Salary },
            new { Id = ExpenseType.CleaningId, Name = "تنظيف", IsActive = true, SortOrder = 5, LegacyCategory = (ExpenseCategory?)null },
            new { Id = ExpenseType.OtherId, Name = "أخرى", IsActive = true, SortOrder = 99, LegacyCategory = (ExpenseCategory?)ExpenseCategory.Other });
    }
}

public class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.ToTable("Employees");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.FullName).IsRequired().HasMaxLength(Employee.MaxNameLength);
        builder.Property(e => e.Phone).HasMaxLength(30);
        builder.Property(e => e.MonthlySalary).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(e => e.Notes).HasMaxLength(500);
        builder.Property(e => e.CreatedAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(e => e.UpdatedAtUtc).HasColumnType("datetime2");

        builder.HasIndex(e => new { e.BranchId, e.IsActive });
        builder.HasOne<Branch>().WithMany().HasForeignKey(e => e.BranchId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class EmployeePaymentConfiguration : IEntityTypeConfiguration<EmployeePayment>
{
    public void Configure(EntityTypeBuilder<EmployeePayment> builder)
    {
        builder.ToTable("EmployeePayments");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Type).HasConversion<int>().IsRequired();
        builder.Property(p => p.GrossAmount).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(p => p.AdvanceDeducted).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(p => p.NetPaid).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(p => p.OccurredAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(p => p.Notes).HasMaxLength(EmployeePayment.MaxNotesLength);

        builder.HasIndex(p => p.ClientRequestId).IsUnique();
        builder.HasIndex(p => new { p.EmployeeId, p.OccurredAtUtc });
        builder.HasIndex(p => new { p.BranchId, p.OccurredAtUtc });

        builder.HasOne<Employee>().WithMany().HasForeignKey(p => p.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Branch>().WithMany().HasForeignKey(p => p.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(p => p.RecordedByUserId).OnDelete(DeleteBehavior.Restrict);
        // سجل تاريخي بحت - بلا تعديل/حذف.
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
        builder.HasIndex(c => c.PartnerId);

        builder.HasOne<User>().WithMany().HasForeignKey(c => c.RecordedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Domain.Partners.Partner>().WithMany().HasForeignKey(c => c.PartnerId).OnDelete(DeleteBehavior.Restrict);
        // Branch FK (Restrict) configured on the Branches side.
        // No update path is exposed anywhere in the model for this entity —
        // append-only by construction (نفس فلسفة CashDrawerLog).
    }
}
