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
        builder.Property(o => o.CreatedAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(o => o.UpdatedAtUtc).HasColumnType("datetime2");

        builder.HasIndex(o => new { o.BranchId, o.Status, o.CreatedAtUtc });
        builder.HasIndex(o => new { o.CustomerId, o.CreatedAtUtc });

        builder.HasOne<Customer>().WithMany().HasForeignKey(o => o.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(o => o.DecidedByUserId).OnDelete(DeleteBehavior.Restrict);
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
