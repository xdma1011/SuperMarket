using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SupermarketSystem.Domain.Catalog;
using SupermarketSystem.Domain.Customers;
using SupermarketSystem.Domain.Identity;
using SupermarketSystem.Domain.Ordering;
using SupermarketSystem.Domain.Sales;

namespace SupermarketSystem.Infrastructure.Persistence.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.Status).HasConversion<int>().IsRequired();
        builder.Property(o => o.DeliveryNote).HasMaxLength(1000);
        builder.Property(o => o.DeliveryLatitude).HasColumnType("decimal(9,6)");
        builder.Property(o => o.DeliveryLongitude).HasColumnType("decimal(9,6)");
        builder.Property(o => o.DecidedAtUtc).HasColumnType("datetime2");
        builder.Property(o => o.RejectionReason).HasMaxLength(500);
        builder.Property(o => o.RatingComment).HasMaxLength(1000);
        builder.Property(o => o.DriverAssignedAtUtc).HasColumnType("datetime2");
        builder.Property(o => o.CreatedAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(o => o.UpdatedAtUtc).HasColumnType("datetime2");

        builder.HasIndex(o => new { o.BranchId, o.Status, o.CreatedAtUtc });
        builder.HasIndex(o => new { o.CustomerId, o.CreatedAtUtc });
        builder.HasIndex(o => new { o.DriverId, o.Status });

        builder.HasOne<Customer>().WithMany().HasForeignKey(o => o.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(o => o.DecidedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(o => o.DriverId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SaleInvoice>().WithMany().HasForeignKey(o => o.ResultingSaleInvoiceId).OnDelete(DeleteBehavior.Restrict);
        // Branch FK (Restrict) configured on the Branches side.

        builder.HasMany(o => o.Items).WithOne().HasForeignKey(i => i.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(o => o.Items).HasField("_items").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("OrderItems");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Quantity).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(i => i.EstimatedUnitPrice).HasColumnType("decimal(18,4)").IsRequired();

        builder.HasIndex(i => i.ProductId);

        builder.HasOne<Product>().WithMany().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductUnit>().WithMany().HasForeignKey(i => i.ProductUnitId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class CouponConfiguration : IEntityTypeConfiguration<Coupon>
{
    public void Configure(EntityTypeBuilder<Coupon> builder)
    {
        builder.ToTable("Coupons");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Code).IsRequired().HasMaxLength(Coupon.MaxCodeLength);
        builder.Property(c => c.Title).IsRequired().HasMaxLength(Coupon.MaxTitleLength);
        builder.Property(c => c.DiscountType).HasConversion<int>().IsRequired();
        builder.Property(c => c.Value).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(c => c.MaxDiscountAmount).HasColumnType("decimal(18,4)");
        builder.Property(c => c.MinOrderAmount).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(c => c.StartAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(c => c.EndAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(c => c.LastSentAtUtc).HasColumnType("datetime2");
        builder.Property(c => c.CreatedAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(c => c.UpdatedAtUtc).HasColumnType("datetime2");

        builder.HasIndex(c => c.Code).IsUnique();
        builder.HasIndex(c => c.CustomerId);

        builder.HasOne<Customer>().WithMany().HasForeignKey(c => c.CustomerId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class CouponRedemptionConfiguration : IEntityTypeConfiguration<CouponRedemption>
{
    public void Configure(EntityTypeBuilder<CouponRedemption> builder)
    {
        builder.ToTable("CouponRedemptions");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Status).HasConversion<int>().IsRequired();
        builder.Property(r => r.EstimatedDiscountAmount).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(r => r.DiscountAmount).HasColumnType("decimal(18,4)");
        builder.Property(r => r.ReservedAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(r => r.ClosedAtUtc).HasColumnType("datetime2");

        // كوبون واحد لكل طلب.
        builder.HasIndex(r => r.OrderId).IsUnique();
        builder.HasIndex(r => new { r.CouponId, r.CustomerId, r.Status });
        builder.HasIndex(r => r.SaleInvoiceId);

        builder.HasOne<Coupon>().WithMany().HasForeignKey(r => r.CouponId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Customer>().WithMany().HasForeignKey(r => r.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Order>().WithMany().HasForeignKey(r => r.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SaleInvoice>().WithMany().HasForeignKey(r => r.SaleInvoiceId).OnDelete(DeleteBehavior.Restrict);
    }
}
