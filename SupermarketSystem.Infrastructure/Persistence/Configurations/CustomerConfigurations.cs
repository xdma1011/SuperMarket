using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SupermarketSystem.Domain.Customers;

namespace SupermarketSystem.Infrastructure.Persistence.Configurations;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.FullName).IsRequired().HasMaxLength(200);
        builder.Property(c => c.Phone).HasMaxLength(30);
        builder.Property(c => c.Email).HasMaxLength(256);
        builder.Property(c => c.IsBlocked).IsRequired();
        builder.Property(c => c.RowVersion).IsRowVersion();
        builder.Property(c => c.CreatedAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(c => c.UpdatedAtUtc).HasColumnType("datetime2");

        // POS lookup by phone — not assumed unique (Architecture Review §13/§14).
        builder.HasIndex(c => c.Phone);
        // Drives the derived purchase-history query (CustomerPurchaseHistory
        // was removed as a stored entity — this index is what keeps that
        // query fast at volume). Also referenced from SaleInvoices(CustomerId, CreatedAtUtc).
        builder.HasIndex(c => c.CreatedAtUtc);

        builder.HasMany(c => c.Notes).WithOne().HasForeignKey(n => n.CustomerId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(c => c.Notes).HasField("_notes").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class CustomerNoteConfiguration : IEntityTypeConfiguration<CustomerNote>
{
    public void Configure(EntityTypeBuilder<CustomerNote> builder)
    {
        builder.ToTable("CustomerNotes");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Text).IsRequired().HasMaxLength(2000);
    }
}

public class ComplaintConfiguration : IEntityTypeConfiguration<Complaint>
{
    public void Configure(EntityTypeBuilder<Complaint> builder)
    {
        builder.ToTable("Complaints");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Text).IsRequired().HasMaxLength(2000);
        builder.Property(c => c.IsResolved).IsRequired();
        builder.Property(c => c.ResolvedAtUtc).HasColumnType("datetime2");
        builder.Property(c => c.CreatedAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(c => c.UpdatedAtUtc).HasColumnType("datetime2");

        builder.HasIndex(c => new { c.IsResolved, c.CreatedAtUtc });

        builder.HasOne<Customer>().WithMany().HasForeignKey(c => c.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SupermarketSystem.Domain.Ordering.Order>().WithMany().HasForeignKey(c => c.OrderId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class TelegramChatLinkConfiguration : IEntityTypeConfiguration<TelegramChatLink>
{
    public void Configure(EntityTypeBuilder<TelegramChatLink> builder)
    {
        builder.ToTable("TelegramChatLinks");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Phone).IsRequired().HasMaxLength(30);
        builder.Property(l => l.ChatId).IsRequired().HasMaxLength(50);
        builder.Property(l => l.LinkedAtUtc).HasColumnType("datetime2").IsRequired();

        // رقم واحد = ربط فعّال واحد (Relink بيحدّث نفس السطر، لا يضيف سطر جديد).
        builder.HasIndex(l => l.Phone).IsUnique();
    }
}

public class CustomerDeviceTokenConfiguration : IEntityTypeConfiguration<CustomerDeviceToken>
{
    public void Configure(EntityTypeBuilder<CustomerDeviceToken> builder)
    {
        builder.ToTable("CustomerDeviceTokens");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Token).IsRequired().HasMaxLength(500);
        builder.Property(t => t.Platform).IsRequired().HasConversion<int>();
        builder.Property(t => t.RegisteredAtUtc).HasColumnType("datetime2").IsRequired();

        // نفس رقم التوكن ممكن يتسجّل لزبونين مختلفين بالتتابع (جهاز مشترك،
        // إعادة تثبيت) - فريد على مستوى Token نفسه لا (CustomerId, Token)،
        // فيمنع تراكم أشباح لتوكن أُعيد استخدامه.
        builder.HasIndex(t => t.Token).IsUnique();
        builder.HasIndex(t => t.CustomerId);

        builder.HasOne<Customer>().WithMany().HasForeignKey(t => t.CustomerId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class CustomerLoyaltyPointsEntryConfiguration : IEntityTypeConfiguration<CustomerLoyaltyPointsEntry>
{
    public void Configure(EntityTypeBuilder<CustomerLoyaltyPointsEntry> builder)
    {
        builder.ToTable("CustomerLoyaltyPointsEntries");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Points).IsRequired();
        builder.Property(e => e.Reason).IsRequired().HasConversion<int>();
        builder.Property(e => e.CreatedAtUtc).HasColumnType("datetime2").IsRequired();

        // الرصيد الحالي = SUM(Points) لكل زبون - هذا الفهرس هو اللي بيخلي
        // ذاك الاستعلام سريع بلا Migration لعمود رصيد منفصل.
        builder.HasIndex(e => e.CustomerId);

        builder.HasOne<Customer>().WithMany().HasForeignKey(e => e.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SupermarketSystem.Domain.Ordering.Order>().WithMany().HasForeignKey(e => e.OrderId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class CustomerOtpCodeConfiguration : IEntityTypeConfiguration<CustomerOtpCode>
{
    public void Configure(EntityTypeBuilder<CustomerOtpCode> builder)
    {
        builder.ToTable("CustomerOtpCodes");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.Phone).IsRequired().HasMaxLength(30);
        builder.Property(o => o.CodeHash).IsRequired().HasMaxLength(100);
        builder.Property(o => o.ExpiresAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(o => o.CreatedAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(o => o.IsUsed).IsRequired();

        // آخر كود فعّال لرقم معيّن - يُقرأ Descending بالإنشاء لجلب الأحدث.
        builder.HasIndex(o => new { o.Phone, o.CreatedAtUtc });
    }
}
