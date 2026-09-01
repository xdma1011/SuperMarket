using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SupermarketSystem.Domain.Catalog;
using SupermarketSystem.Domain.Identity;
using SupermarketSystem.Domain.Payments;
using SupermarketSystem.Domain.Purchasing;

namespace SupermarketSystem.Infrastructure.Persistence.Configurations;

public class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("Suppliers");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Name).IsRequired().HasMaxLength(200);
        builder.Property(s => s.ContactName).HasMaxLength(200);
        builder.Property(s => s.Phone).HasMaxLength(30);
        builder.Property(s => s.Email).HasMaxLength(256);
        builder.Property(s => s.RowVersion).IsRowVersion();
        builder.Property(s => s.CreatedAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(s => s.UpdatedAtUtc).HasColumnType("datetime2");

        builder.HasIndex(s => s.Name);

        builder.OwnsOne(s => s.Address, a =>
        {
            a.Property(x => x.Street).HasMaxLength(300).HasColumnName("Address_Street");
            a.Property(x => x.City).HasMaxLength(100).HasColumnName("Address_City");
            a.Property(x => x.PostalCode).HasMaxLength(20).HasColumnName("Address_PostalCode");
            a.Property(x => x.Country).HasMaxLength(100).HasColumnName("Address_Country");
        });
    }
}

public class PurchaseInvoiceConfiguration : IEntityTypeConfiguration<PurchaseInvoice>
{
    public void Configure(EntityTypeBuilder<PurchaseInvoice> builder)
    {
        builder.ToTable("PurchaseInvoices");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.InvoiceNumber).IsRequired().HasMaxLength(50);
        builder.Property(p => p.SupplierInvoiceReference).HasMaxLength(100);
        builder.Property(p => p.Status).HasConversion<int>().IsRequired();
        builder.Property(p => p.TotalAmount).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(p => p.RowVersion).IsRowVersion();
        builder.Property(p => p.CreatedAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(p => p.UpdatedAtUtc).HasColumnType("datetime2");

        builder.HasIndex(p => new { p.BranchId, p.InvoiceNumber }).IsUnique();
        builder.HasIndex(p => p.SupplierId);
        builder.HasIndex(p => new { p.BranchId, p.CreatedAtUtc });

        builder.HasOne<Supplier>().WithMany().HasForeignKey(p => p.SupplierId).OnDelete(DeleteBehavior.Restrict);
        // Branch FK (Restrict) configured on the Branches side.

        builder.HasMany(p => p.Items).WithOne().HasForeignKey(i => i.PurchaseInvoiceId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Items).HasField("_items").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(p => p.Payments).WithOne().HasForeignKey(pay => pay.PurchaseInvoiceId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Payments).HasField("_payments").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Property(p => p.TotalPaidAmount).HasColumnType("decimal(18,4)").IsRequired();

        builder.HasMany(p => p.Images).WithOne().HasForeignKey(i => i.PurchaseInvoiceId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Images).HasField("_images").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class PurchaseInvoicePaymentConfiguration : IEntityTypeConfiguration<PurchaseInvoicePayment>
{
    public void Configure(EntityTypeBuilder<PurchaseInvoicePayment> builder)
    {
        builder.ToTable("PurchaseInvoicePayments");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Amount).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(p => p.CreatedAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(p => p.ExternalReference).HasMaxLength(200);
        builder.Property(p => p.RowVersion).IsRowVersion();

        // Idempotency key — نفس مبدأ SaleInvoicePayment بالضبط (راجع
        // التعليق بالكيان نفسه).
        builder.HasIndex(p => p.ClientRequestId).IsUnique();
        builder.HasIndex(p => new { p.BranchId, p.CreatedAtUtc });
        builder.HasIndex(p => new { p.PaymentMethodId, p.CreatedAtUtc });

        builder.HasOne<PaymentMethod>().WithMany().HasForeignKey(p => p.PaymentMethodId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Restrict);
        // Branch FK (Restrict) configured on the Branches side.
    }
}

public class PurchaseInvoiceItemConfiguration : IEntityTypeConfiguration<PurchaseInvoiceItem>
{
    public void Configure(EntityTypeBuilder<PurchaseInvoiceItem> builder)
    {
        builder.ToTable("PurchaseInvoiceItems");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Quantity).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(i => i.UnitCost).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(i => i.LineTotal).HasColumnType("decimal(18,4)").IsRequired();

        builder.HasIndex(i => i.ProductId);

        builder.HasOne<Product>().WithMany().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductUnit>().WithMany().HasForeignKey(i => i.ProductUnitId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Domain.Inventory.ProductBatch>().WithMany().HasForeignKey(i => i.ProductBatchId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class PurchaseInvoiceImageConfiguration : IEntityTypeConfiguration<PurchaseInvoiceImage>
{
    public void Configure(EntityTypeBuilder<PurchaseInvoiceImage> builder)
    {
        builder.ToTable("PurchaseInvoiceImages");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Url).IsRequired().HasMaxLength(1000);
    }
}

public class PurchaseInvoiceDraftConfiguration : IEntityTypeConfiguration<PurchaseInvoiceDraft>
{
    public void Configure(EntityTypeBuilder<PurchaseInvoiceDraft> builder)
    {
        builder.ToTable("PurchaseInvoiceDrafts");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.ImageReference).IsRequired().HasMaxLength(1000);
        builder.Property(d => d.ProviderName).HasMaxLength(50);
        builder.Property(d => d.RawSupplierName).HasMaxLength(200);
        builder.Property(d => d.SupplierInvoiceReference).HasMaxLength(100);
        builder.Property(d => d.Currency).HasMaxLength(10);
        builder.Property(d => d.ExtractedInvoiceTotal).HasColumnType("decimal(18,4)");
        builder.Property(d => d.ExtractionConfidence).HasMaxLength(20);
        builder.Property(d => d.WarningsText).HasMaxLength(2000);
        // لا حد أقصى صريح للطول - فاتورة بعشرات الأسطر ممكن تتجاوز
        // nvarchar(4000) بسهولة؛ nvarchar(max) الافتراضي هون مقصود.
        builder.Property(d => d.ItemsJson).IsRequired();
        builder.Property(d => d.Status).HasConversion<int>().IsRequired();
        builder.Property(d => d.RowVersion).IsRowVersion();
        builder.Property(d => d.CreatedAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(d => d.UpdatedAtUtc).HasColumnType("datetime2");

        builder.HasIndex(d => new { d.BranchId, d.Status, d.CreatedAtUtc });

        builder.HasOne<Supplier>().WithMany().HasForeignKey(d => d.MatchedSupplierId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<PurchaseInvoice>().WithMany().HasForeignKey(d => d.ResultingPurchaseInvoiceId).OnDelete(DeleteBehavior.SetNull);
        // Branch FK (Restrict) configured on the Branches side.
    }
}
