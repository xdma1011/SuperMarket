using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SupermarketSystem.Domain.Catalog;

namespace SupermarketSystem.Infrastructure.Persistence.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name).IsRequired().HasMaxLength(300);
        builder.Property(p => p.Description).HasMaxLength(2000);
        builder.Property(p => p.Status).HasConversion<int>().IsRequired();
        // SuggestedRetailPrice: catalog-level reference value only, never
        // read at sale time (Architecture Review §1/§2 v2).
        builder.Property(p => p.SuggestedRetailPrice).HasColumnType("decimal(18,4)");
        // ExpectedShelfLifeDays: int? عادي، بلا إعداد خاص مطلوب — nullable
        // تلقائيًا بحكم نوع الـCLR نفسه.
        builder.Property(p => p.RowVersion).IsRowVersion();
        builder.Property(p => p.CreatedAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(p => p.UpdatedAtUtc).HasColumnType("datetime2");

        builder.HasIndex(p => p.Name);
        builder.HasIndex(p => p.CategoryId);
        // كان ناقص — لازم لأي فرز/فلترة "الأحدث إضافة" لو الكتالوج كبر
        // لآلاف الأصناف. باقي الجداول التجارية (المبيعات، الحركات، إلخ)
        // كانت مغطّاة أصلًا؛ Products كانت الاستثناء الوحيد المكتشف.
        builder.HasIndex(p => p.CreatedAtUtc);

        builder.HasOne<ProductCategory>()
            .WithMany()
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        // Aggregate-internal children -> Cascade.
        builder.HasMany(p => p.Units).WithOne().HasForeignKey(u => u.ProductId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Units).HasField("_units").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(p => p.Barcodes).WithOne().HasForeignKey(b => b.ProductId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Barcodes).HasField("_barcodes").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(p => p.Images).WithOne().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Images).HasField("_images").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(p => p.Notes).WithOne().HasForeignKey(n => n.ProductId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Notes).HasField("_notes").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class ProductUnitConfiguration : IEntityTypeConfiguration<ProductUnit>
{
    public void Configure(EntityTypeBuilder<ProductUnit> builder)
    {
        builder.ToTable("ProductUnits");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.UnitName).IsRequired().HasMaxLength(50);
        // Finer precision than money — some conversions need it (Architecture Review §8).
        builder.Property(u => u.ConversionFactorToBase).HasColumnType("decimal(18,6)").IsRequired();
    }
}

public class ProductBarcodeConfiguration : IEntityTypeConfiguration<ProductBarcode>
{
    public void Configure(EntityTypeBuilder<ProductBarcode> builder)
    {
        builder.ToTable("ProductBarcodes");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.BarcodeValue).IsRequired().HasMaxLength(100);
        builder.HasIndex(b => b.BarcodeValue).IsUnique();

        builder.HasOne<ProductUnit>()
            .WithMany()
            .HasForeignKey(b => b.ProductUnitId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ProductImageConfiguration : IEntityTypeConfiguration<ProductImage>
{
    public void Configure(EntityTypeBuilder<ProductImage> builder)
    {
        builder.ToTable("ProductImages");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Url).IsRequired().HasMaxLength(1000);
    }
}

public class ProductNoteConfiguration : IEntityTypeConfiguration<ProductNote>
{
    public void Configure(EntityTypeBuilder<ProductNote> builder)
    {
        builder.ToTable("ProductNotes");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Text).IsRequired().HasMaxLength(2000);
    }
}

public class ProductCategoryConfiguration : IEntityTypeConfiguration<ProductCategory>
{
    public void Configure(EntityTypeBuilder<ProductCategory> builder)
    {
        builder.ToTable("ProductCategories");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name).IsRequired().HasMaxLength(200);
        builder.Property(c => c.RowVersion).IsRowVersion();
        builder.Property(c => c.CreatedAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(c => c.UpdatedAtUtc).HasColumnType("datetime2");

        // Self-referencing hierarchy -> Restrict (deleting a parent category
        // while children reference it is not allowed).
        builder.HasOne<ProductCategory>()
            .WithMany()
            .HasForeignKey(c => c.ParentCategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ProductBranchConfiguration : IEntityTypeConfiguration<ProductBranch>
{
    public void Configure(EntityTypeBuilder<ProductBranch> builder)
    {
        builder.ToTable("ProductBranches");
        builder.HasKey(pb => pb.Id);

        builder.Property(pb => pb.SellingPrice).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(pb => pb.MinimumStock).HasColumnType("decimal(18,4)");
        builder.Property(pb => pb.MaximumStock).HasColumnType("decimal(18,4)");
        builder.Property(pb => pb.RowVersion).IsRowVersion();
        builder.Property(pb => pb.CreatedAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(pb => pb.UpdatedAtUtc).HasColumnType("datetime2");

        // This row's natural key — a product isn't sellable at a branch
        // until it exists (Architecture Review §1 v2).
        builder.HasIndex(pb => new { pb.ProductId, pb.BranchId }).IsUnique();
        // "List sellable products at this branch" — the core POS listing query.
        builder.HasIndex(pb => new { pb.BranchId, pb.IsAvailableForSale });

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(pb => pb.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
        // Branch FK (Restrict) configured on the Branches side.
    }
}
