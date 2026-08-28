using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SupermarketSystem.Domain.Catalog;
using SupermarketSystem.Domain.Identity;
using SupermarketSystem.Domain.Inventory;

namespace SupermarketSystem.Infrastructure.Persistence.Configurations;

public class ProductBatchConfiguration : IEntityTypeConfiguration<ProductBatch>
{
    public void Configure(EntityTypeBuilder<ProductBatch> builder)
    {
        builder.ToTable("ProductBatches");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.BatchNumber).IsRequired().HasMaxLength(100);
        builder.Property(b => b.UnitCost).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(b => b.CreatedAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(b => b.UpdatedAtUtc).HasColumnType("datetime2");

        builder.HasIndex(b => new { b.BranchId, b.ProductId });

        builder.HasOne<Product>().WithMany().HasForeignKey(b => b.ProductId).OnDelete(DeleteBehavior.Restrict);
        // Branch FK (Restrict) configured on the Branches side.
    }
}

public class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("StockMovements");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.QuantityBase).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(m => m.MovementType).HasConversion<int>().IsRequired();
        builder.Property(m => m.Reason).HasMaxLength(500);
        builder.Property(m => m.OccurredAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(m => m.ReferenceType).HasConversion<int>().IsRequired();
        // ReferenceId is a deliberate loose reference — no FK (see class remarks in Domain).
        builder.Property(m => m.NeedsReview).IsRequired();
        builder.Property(m => m.ReviewedAtUtc).HasColumnType("datetime2");

        // "قائمة المراجعات المعلَّقة" (unified reviews page) بتستعلم بهذا
        // الشرط تحديدًا — فهرس مخصص يخليها سريعة حتى مع تراكم آلاف السجلات
        // التاريخية.
        builder.HasIndex(m => m.NeedsReview);

        // Covers "movement history for this product at this branch, ordered by time".
        builder.HasIndex(m => new { m.BranchId, m.ProductId, m.OccurredAtUtc });
        builder.HasIndex(m => m.ProductBatchId);
        builder.HasIndex(m => new { m.ReferenceType, m.ReferenceId });

        builder.HasOne<Product>().WithMany().HasForeignKey(m => m.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductUnit>().WithMany().HasForeignKey(m => m.ProductUnitId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Domain.Inventory.ProductBatch>().WithMany().HasForeignKey(m => m.ProductBatchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Restrict);
        // Branch FK (Restrict) configured on the Branches side.
    }
}

public class StockConfiguration : IEntityTypeConfiguration<Stock>
{
    public void Configure(EntityTypeBuilder<Stock> builder)
    {
        builder.ToTable("Stocks");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.QuantityOnHand).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(s => s.RowVersion).IsRowVersion();

        // This row's natural key is (ProductId, BranchId, ProductBatchId),
        // but ProductBatchId is nullable and SQL Server's default unique
        // index treats every row containing a NULL as distinct from every
        // other, so a single unique index across all three columns would
        // NOT prevent duplicate (Product, Branch, NULL) balance rows. Two
        // filtered unique indexes close that gap explicitly.
        builder.HasIndex(s => new { s.ProductId, s.BranchId })
            .IsUnique()
            .HasFilter("[ProductBatchId] IS NULL")
            .HasDatabaseName("IX_Stocks_ProductId_BranchId_NoBatch");

        builder.HasIndex(s => new { s.ProductId, s.BranchId, s.ProductBatchId })
            .IsUnique()
            .HasFilter("[ProductBatchId] IS NOT NULL")
            .HasDatabaseName("IX_Stocks_ProductId_BranchId_ProductBatchId");

        builder.HasOne<Product>().WithMany().HasForeignKey(s => s.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Domain.Inventory.ProductBatch>().WithMany().HasForeignKey(s => s.ProductBatchId).OnDelete(DeleteBehavior.Restrict);
        // Branch FK (Restrict) configured on the Branches side.
    }
}

public class StocktakeConfiguration : IEntityTypeConfiguration<Stocktake>
{
    public void Configure(EntityTypeBuilder<Stocktake> builder)
    {
        builder.ToTable("Stocktakes");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.StocktakeNumber).IsRequired().HasMaxLength(50);
        builder.Property(s => s.Status).HasConversion<int>().IsRequired();
        builder.Property(s => s.CompletedAtUtc).HasColumnType("datetime2");
        builder.Property(s => s.ApprovedAtUtc).HasColumnType("datetime2");
        builder.Property(s => s.RowVersion).IsRowVersion();
        builder.Property(s => s.CreatedAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(s => s.UpdatedAtUtc).HasColumnType("datetime2");

        builder.HasIndex(s => new { s.BranchId, s.StocktakeNumber }).IsUnique();
        builder.HasIndex(s => new { s.BranchId, s.Status, s.CreatedAtUtc });

        builder.HasOne<User>().WithMany().HasForeignKey(s => s.ApprovedByUserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(s => s.Items).WithOne().HasForeignKey(i => i.StocktakeId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(s => s.Items).HasField("_items").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class StocktakeItemConfiguration : IEntityTypeConfiguration<StocktakeItem>
{
    public void Configure(EntityTypeBuilder<StocktakeItem> builder)
    {
        builder.ToTable("StocktakeItems");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.ExpectedQuantity).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(i => i.CountedQuantity).HasColumnType("decimal(18,4)");
        builder.Property(i => i.CountedAtUtc).HasColumnType("datetime2");

        // Derived, not stored (Domain remarks) — no atomicity guard needed.
        builder.Ignore(i => i.VarianceQuantity);

        builder.HasOne<Product>().WithMany().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Domain.Inventory.ProductBatch>().WithMany().HasForeignKey(i => i.ProductBatchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(i => i.CountedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
