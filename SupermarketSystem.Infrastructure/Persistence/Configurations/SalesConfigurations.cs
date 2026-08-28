using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SupermarketSystem.Domain.Branches;
using SupermarketSystem.Domain.Catalog;
using SupermarketSystem.Domain.Customers;
using SupermarketSystem.Domain.Identity;
using SupermarketSystem.Domain.Payments;
using SupermarketSystem.Domain.Sales;

namespace SupermarketSystem.Infrastructure.Persistence.Configurations;

public class SaleInvoiceConfiguration : IEntityTypeConfiguration<SaleInvoice>
{
    public void Configure(EntityTypeBuilder<SaleInvoice> builder)
    {
        builder.ToTable("SaleInvoices");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.InvoiceNumber).IsRequired().HasMaxLength(50);
        builder.Property(s => s.CustomerNameSnapshot).HasMaxLength(200);
        builder.Property(s => s.CustomerPhoneSnapshot).HasMaxLength(30);
        builder.Property(s => s.Status).HasConversion<int>().IsRequired();
        builder.Property(s => s.TotalAmount).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(s => s.DiscountAmountSnapshot).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(s => s.TotalPaidAmount).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(s => s.TotalReturnedAmount).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(s => s.VoidedAtUtc).HasColumnType("datetime2");
        builder.Property(s => s.VoidReason).HasConversion<int?>();
        builder.Property(s => s.VoidNotes).HasMaxLength(500);
        builder.Property(s => s.RowVersion).IsRowVersion();
        builder.Property(s => s.CreatedAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(s => s.UpdatedAtUtc).HasColumnType("datetime2");

        builder.HasIndex(s => new { s.BranchId, s.InvoiceNumber }).IsUnique();
        // Idempotency key (Architecture Review §16). Unique at the DATABASE
        // level, not merely checked in application code — under concurrent
        // double-submission the pre-check can lose the race, and this
        // constraint is what actually stops the second invoice from
        // existing. The handler translates the resulting violation into a
        // replay of the original sale rather than an error.
        builder.HasIndex(s => s.ClientRequestId).IsUnique();
        // The minority statuses (Voided/PartiallyReturned/FullyReturned) are
        // exactly what this index makes cheap to find (Architecture Review §14).
        builder.HasIndex(s => new { s.BranchId, s.Status, s.CreatedAtUtc });
        builder.HasIndex(s => s.CustomerId);

        builder.HasOne<Customer>().WithMany().HasForeignKey(s => s.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Discount>().WithMany().HasForeignKey(s => s.DiscountId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<User>().WithMany().HasForeignKey(s => s.VoidedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Branch>().WithMany().HasForeignKey(s => s.BranchId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(s => s.Items).WithOne().HasForeignKey(i => i.SaleInvoiceId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(s => s.Items).HasField("_items").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(s => s.Payments).WithOne().HasForeignKey(p => p.SaleInvoiceId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(s => s.Payments).HasField("_payments").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class SaleInvoiceItemConfiguration : IEntityTypeConfiguration<SaleInvoiceItem>
{
    public void Configure(EntityTypeBuilder<SaleInvoiceItem> builder)
    {
        builder.ToTable("SaleInvoiceItems");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Quantity).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(i => i.UnitPriceSnapshot).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(i => i.DiscountSnapshot).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(i => i.LineTotal).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(i => i.QuantityReturned).HasColumnType("decimal(18,4)").IsRequired();

        builder.HasIndex(i => i.ProductId);

        builder.HasOne<Product>().WithMany().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductUnit>().WithMany().HasForeignKey(i => i.ProductUnitId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Discount>().WithMany().HasForeignKey(i => i.DiscountId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class SaleInvoicePaymentConfiguration : IEntityTypeConfiguration<SaleInvoicePayment>
{
    public void Configure(EntityTypeBuilder<SaleInvoicePayment> builder)
    {
        builder.ToTable("SaleInvoicePayments");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Amount).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(p => p.CreatedAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(p => p.Status).HasConversion<int>().IsRequired();
        builder.Property(p => p.ExternalReference).HasMaxLength(200);
        builder.Property(p => p.ReversedAtUtc).HasColumnType("datetime2");
        builder.Property(p => p.ReversedReason).HasMaxLength(500);
        builder.Property(p => p.RowVersion).IsRowVersion();

        // Idempotency key — a retried submission (network failure,
        // double-click) is detected via this unique constraint rather than
        // creating a duplicate payment (Architecture Review §16.3/§16.12).
        builder.HasIndex(p => p.ClientRequestId).IsUnique();
        builder.HasIndex(p => new { p.BranchId, p.CreatedAtUtc });
        builder.HasIndex(p => new { p.PaymentMethodId, p.CreatedAtUtc });
        builder.HasIndex(p => new { p.UserId, p.CreatedAtUtc });
        builder.HasIndex(p => p.ExternalReference);

        builder.HasOne<PaymentMethod>().WithMany().HasForeignKey(p => p.PaymentMethodId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Branch>().WithMany().HasForeignKey(p => p.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(p => p.ReversedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class SuspendedSaleConfiguration : IEntityTypeConfiguration<SuspendedSale>
{
    public void Configure(EntityTypeBuilder<SuspendedSale> builder)
    {
        builder.ToTable("SuspendedSales");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.CreatedAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(s => s.UpdatedAtUtc).HasColumnType("datetime2");

        builder.HasIndex(s => new { s.BranchId, s.UserId });

        builder.HasOne<User>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Restrict);
        // Branch FK (Restrict) configured on the Branches side. Pre-
        // transactional data (Architecture Review §"SuspendedSale") — not
        // subject to the "never delete history" rule, so a Cascade from
        // Branch/User could in principle be argued; Restrict is kept for
        // consistency with every other branch/user reference in the model.

        builder.HasMany(s => s.Items).WithOne().HasForeignKey(i => i.SuspendedSaleId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(s => s.Items).HasField("_items").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class SuspendedSaleItemConfiguration : IEntityTypeConfiguration<SuspendedSaleItem>
{
    public void Configure(EntityTypeBuilder<SuspendedSaleItem> builder)
    {
        builder.ToTable("SuspendedSaleItems");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Quantity).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(i => i.UnitPriceSnapshot).HasColumnType("decimal(18,4)").IsRequired();

        builder.HasOne<Product>().WithMany().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductUnit>().WithMany().HasForeignKey(i => i.ProductUnitId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class DiscountConfiguration : IEntityTypeConfiguration<Discount>
{
    public void Configure(EntityTypeBuilder<Discount> builder)
    {
        builder.ToTable("Discounts");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Name).IsRequired().HasMaxLength(200);
        builder.Property(d => d.Type).HasConversion<int>().IsRequired();
        // One column serves both Percentage (e.g. 10.0000 = 10%) and
        // FixedAmount depending on Type — decimal(18,4) is used uniformly
        // so a FixedAmount value is never truncated; a Percentage-only
        // column would instead use decimal(5,2) per the precision table.
        builder.Property(d => d.Value).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(d => d.RowVersion).IsRowVersion();
        builder.Property(d => d.CreatedAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(d => d.UpdatedAtUtc).HasColumnType("datetime2");

        // Branch FK (Restrict, nullable — global by default) configured on the Branches side.
    }
}

public class ReturnInvoiceConfiguration : IEntityTypeConfiguration<ReturnInvoice>
{
    public void Configure(EntityTypeBuilder<ReturnInvoice> builder)
    {
        builder.ToTable("ReturnInvoices");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.InvoiceNumber).IsRequired().HasMaxLength(50);
        builder.Property(r => r.Reason).HasConversion<int>().IsRequired();
        builder.Property(r => r.Notes).HasMaxLength(1000);
        builder.Property(r => r.TotalAmount).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(r => r.TotalRefundedAmount).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(r => r.RowVersion).IsRowVersion();
        builder.Property(r => r.CreatedAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(r => r.UpdatedAtUtc).HasColumnType("datetime2");

        builder.HasIndex(r => new { r.BranchId, r.InvoiceNumber }).IsUnique();
        builder.HasIndex(r => new { r.BranchId, r.CreatedAtUtc });
        builder.HasIndex(r => r.OriginalSaleInvoiceId);

        builder.Property(r => r.ReviewedAtUtc).HasColumnType("datetime2");

        // مفتاح idempotency — فريد على مستوى قاعدة البيانات، لا مجرد فحص
        // بالتطبيق: تحت ضغطتين متزامنتين، الفحص التطبيقي ممكن يخسر السباق،
        // وهذا القيد هو اللي فعليًا بيمنع وجود إرجاعين.
        builder.HasIndex(r => r.ClientRequestId).IsUnique();

        // مراجعة الإرجاع إجراء إداري لاحق — المستخدم المراجِع لا يُحذف
        // أبدًا وسجل المراجعة يبقى.
        builder.HasOne<Domain.Identity.User>().WithMany().HasForeignKey(r => r.ReviewedByUserId).OnDelete(DeleteBehavior.Restrict);

        // Cannot delete a sale that has a return against it.
        builder.HasOne<SaleInvoice>().WithMany().HasForeignKey(r => r.OriginalSaleInvoiceId).OnDelete(DeleteBehavior.Restrict);
        // Branch FK (Restrict) configured on the Branches side.

        builder.HasMany(r => r.Items).WithOne().HasForeignKey(i => i.ReturnInvoiceId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(r => r.Items).HasField("_items").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(r => r.Payments).WithOne().HasForeignKey(p => p.ReturnInvoiceId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(r => r.Payments).HasField("_payments").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class ReturnInvoiceItemConfiguration : IEntityTypeConfiguration<ReturnInvoiceItem>
{
    public void Configure(EntityTypeBuilder<ReturnInvoiceItem> builder)
    {
        builder.ToTable("ReturnInvoiceItems");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Quantity).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(i => i.UnitPriceSnapshot).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(i => i.LineTotal).HasColumnType("decimal(18,4)").IsRequired();

        // "Return frequency by product" (Architecture Review §14).
        builder.HasIndex(i => i.ProductId);

        builder.HasOne<SaleInvoiceItem>().WithMany().HasForeignKey(i => i.SaleInvoiceItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>().WithMany().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductUnit>().WithMany().HasForeignKey(i => i.ProductUnitId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class ReturnInvoicePaymentConfiguration : IEntityTypeConfiguration<ReturnInvoicePayment>
{
    public void Configure(EntityTypeBuilder<ReturnInvoicePayment> builder)
    {
        builder.ToTable("ReturnInvoicePayments");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Amount).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(p => p.CreatedAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(p => p.Status).HasConversion<int>().IsRequired();
        builder.Property(p => p.ExternalReference).HasMaxLength(200);
        builder.Property(p => p.ReversedAtUtc).HasColumnType("datetime2");
        builder.Property(p => p.ReversedReason).HasMaxLength(500);
        builder.Property(p => p.RowVersion).IsRowVersion();

        builder.HasIndex(p => p.ClientRequestId).IsUnique();
        builder.HasIndex(p => new { p.BranchId, p.CreatedAtUtc });
        builder.HasIndex(p => new { p.PaymentMethodId, p.CreatedAtUtc });
        builder.HasIndex(p => new { p.UserId, p.CreatedAtUtc });
        builder.HasIndex(p => p.ExternalReference);

        builder.HasOne<PaymentMethod>().WithMany().HasForeignKey(p => p.PaymentMethodId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Branch>().WithMany().HasForeignKey(p => p.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(p => p.ReversedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
