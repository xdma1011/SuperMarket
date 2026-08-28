using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SupermarketSystem.Domain.Identity;
using SupermarketSystem.Domain.Notifications;

namespace SupermarketSystem.Infrastructure.Persistence.Configurations;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications");
        builder.HasKey(n => n.Id);

        builder.Property(n => n.Title).IsRequired().HasMaxLength(200);
        builder.Property(n => n.Message).IsRequired().HasMaxLength(2000);
        builder.Property(n => n.Channel).HasConversion<int>().IsRequired();
        builder.Property(n => n.Status).HasConversion<int>().IsRequired();
        builder.Property(n => n.ReadAtUtc).HasColumnType("datetime2");
        builder.Property(n => n.CreatedAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(n => n.UpdatedAtUtc).HasColumnType("datetime2");

        // فهرس قديم من تصميم Phase C — كان يخدم استهداف مستخدم محدد.
        // TargetUserId صار غالبًا null بعد قرار التنبيهات العامة (راجع
        // تعليق Notification.TargetUserId)، فهذا الفهرس حاليًا غير مستغَل
        // فعليًا. أبقيته بدل حذفه — بيصير مفيد فورًا لما توجد مصادقة
        // حقيقية وتنبيهات مستهدِفة لمستخدم بعينه (D11).
        builder.HasIndex(n => new { n.TargetUserId, n.Status });

        // الفهرس اللي يخدم نمط الاستعلام الفعلي الحالي فعليًا —
        // GetNotificationsHandler بيفلتر بالقناة (InApp) وبيرتّب حسب
        // CreatedAtUtc تنازليًا. بدونه، كل طلب polling كان رح يعمل مسح
        // كامل للجدول (table scan) بدل استخدام فهرس.
        builder.HasIndex(n => new { n.Channel, n.CreatedAtUtc });

        builder.HasOne<User>().WithMany().HasForeignKey(n => n.TargetUserId).OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(n => n.DeliveryAttempts).WithOne().HasForeignKey(l => l.NotificationId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(n => n.DeliveryAttempts).HasField("_deliveryAttempts").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class NotificationLogConfiguration : IEntityTypeConfiguration<NotificationLog>
{
    public void Configure(EntityTypeBuilder<NotificationLog> builder)
    {
        builder.ToTable("NotificationLogs");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.AttemptedAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(l => l.ErrorMessage).HasMaxLength(1000);
    }
}
