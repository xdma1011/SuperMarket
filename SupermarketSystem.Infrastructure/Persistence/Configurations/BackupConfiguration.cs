using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SupermarketSystem.Domain.Backups;

namespace SupermarketSystem.Infrastructure.Persistence.Configurations;

public class DatabaseBackupConfiguration : IEntityTypeConfiguration<DatabaseBackup>
{
    public void Configure(EntityTypeBuilder<DatabaseBackup> builder)
    {
        builder.ToTable("DatabaseBackups");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.FileName).IsRequired().HasMaxLength(260);
        builder.Property(b => b.FilePath).IsRequired().HasMaxLength(1000);
        builder.Property(b => b.Status).HasConversion<int>().IsRequired();
        builder.Property(b => b.ErrorMessage).HasMaxLength(2000);
        builder.Property(b => b.CreatedAtUtc).HasColumnType("datetime2").IsRequired();

        // نمط الوصول الوحيد المتوقَّع: "أحدث النسخ أول" — سواء بعرض القائمة
        // أو بتحديد الأقدم للحذف عند تجاوز حد الاحتفاظ.
        builder.HasIndex(b => b.CreatedAtUtc);
    }
}
