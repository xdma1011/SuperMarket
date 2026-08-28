using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SupermarketSystem.Domain.Branches;
using SupermarketSystem.Domain.CashManagement;
using SupermarketSystem.Domain.Catalog;
using SupermarketSystem.Domain.Common;
using SupermarketSystem.Domain.Identity;
using SupermarketSystem.Domain.Inventory;
using SupermarketSystem.Domain.Purchasing;
using SupermarketSystem.Domain.Sales;

namespace SupermarketSystem.Infrastructure.Persistence.Configurations;

// Every Branch relationship in the model is collected here rather than
// scattered across the per-context configuration files. Two reasons:
//
//   1. Branch is referenced from ~14 entities across 7 bounded contexts.
//      The Architecture Review's single most important delete-behavior rule
//      is "a Branch is never hard-deleted while history exists" — having
//      every one of those relationships in one file makes that rule
//      auditable at a glance instead of requiring a hunt through seven
//      files to confirm nothing accidentally cascades.
//   2. It keeps the bounded-context configuration files focused on their
//      own aggregate's concerns.
//
// EF Core supports multiple IEntityTypeConfiguration<T> for the same entity
// type — ApplyConfigurationsFromAssembly runs all of them and merges the
// result, so these compose with the per-context configurations rather than
// replacing them.
//
// EVERY relationship below is Restrict. None is Cascade. That is deliberate
// and load-bearing: a cascade from Branch would delete sales invoices,
// payments, stock ledgers and cash logs, which is exactly the accidental
// destruction of financial history the Architecture Review forbids.

public class BranchOwnedProductBranchConfiguration : IEntityTypeConfiguration<ProductBranch>
{
    public void Configure(EntityTypeBuilder<ProductBranch> builder) =>
        builder.HasOne<Branch>().WithMany().HasForeignKey(e => e.BranchId).OnDelete(DeleteBehavior.Restrict);
}

public class BranchOwnedProductBatchConfiguration : IEntityTypeConfiguration<ProductBatch>
{
    public void Configure(EntityTypeBuilder<ProductBatch> builder) =>
        builder.HasOne<Branch>().WithMany().HasForeignKey(e => e.BranchId).OnDelete(DeleteBehavior.Restrict);
}

public class BranchOwnedStockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder) =>
        builder.HasOne<Branch>().WithMany().HasForeignKey(e => e.BranchId).OnDelete(DeleteBehavior.Restrict);
}

public class BranchOwnedStockConfiguration : IEntityTypeConfiguration<Stock>
{
    public void Configure(EntityTypeBuilder<Stock> builder) =>
        builder.HasOne<Branch>().WithMany().HasForeignKey(e => e.BranchId).OnDelete(DeleteBehavior.Restrict);
}

public class BranchOwnedStocktakeConfiguration : IEntityTypeConfiguration<Stocktake>
{
    public void Configure(EntityTypeBuilder<Stocktake> builder) =>
        builder.HasOne<Branch>().WithMany().HasForeignKey(e => e.BranchId).OnDelete(DeleteBehavior.Restrict);
}

public class BranchOwnedPurchaseInvoiceConfiguration : IEntityTypeConfiguration<PurchaseInvoice>
{
    public void Configure(EntityTypeBuilder<PurchaseInvoice> builder) =>
        builder.HasOne<Branch>().WithMany().HasForeignKey(e => e.BranchId).OnDelete(DeleteBehavior.Restrict);
}

public class BranchOwnedSuspendedSaleConfiguration : IEntityTypeConfiguration<SuspendedSale>
{
    public void Configure(EntityTypeBuilder<SuspendedSale> builder) =>
        builder.HasOne<Branch>().WithMany().HasForeignKey(e => e.BranchId).OnDelete(DeleteBehavior.Restrict);
}

public class BranchOwnedReturnInvoiceConfiguration : IEntityTypeConfiguration<ReturnInvoice>
{
    public void Configure(EntityTypeBuilder<ReturnInvoice> builder) =>
        builder.HasOne<Branch>().WithMany().HasForeignKey(e => e.BranchId).OnDelete(DeleteBehavior.Restrict);
}

public class BranchOwnedCashDrawerLogConfiguration : IEntityTypeConfiguration<CashDrawerLog>
{
    public void Configure(EntityTypeBuilder<CashDrawerLog> builder) =>
        builder.HasOne<Branch>().WithMany().HasForeignKey(e => e.BranchId).OnDelete(DeleteBehavior.Restrict);
}

public class BranchOwnedCashClosingConfiguration : IEntityTypeConfiguration<CashClosing>
{
    public void Configure(EntityTypeBuilder<CashClosing> builder) =>
        builder.HasOne<Branch>().WithMany().HasForeignKey(e => e.BranchId).OnDelete(DeleteBehavior.Restrict);
}

public class BranchOwnedBranchDocumentSequenceConfiguration : IEntityTypeConfiguration<BranchDocumentSequence>
{
    public void Configure(EntityTypeBuilder<BranchDocumentSequence> builder) =>
        builder.HasOne<Branch>().WithMany().HasForeignKey(e => e.BranchId).OnDelete(DeleteBehavior.Restrict);
}

public class BranchOwnedUserBranchConfiguration : IEntityTypeConfiguration<UserBranch>
{
    public void Configure(EntityTypeBuilder<UserBranch> builder) =>
        builder.HasOne<Branch>().WithMany().HasForeignKey(e => e.BranchId).OnDelete(DeleteBehavior.Restrict);
}

// Nullable BranchId references — same Restrict rule, optional relationship.

public class BranchOptionalDiscountConfiguration : IEntityTypeConfiguration<Discount>
{
    public void Configure(EntityTypeBuilder<Discount> builder) =>
        builder.HasOne<Branch>().WithMany().HasForeignKey(e => e.BranchId).OnDelete(DeleteBehavior.Restrict);
}

public class BranchOptionalUserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder) =>
        builder.HasOne<Branch>().WithMany().HasForeignKey(e => e.BranchId).OnDelete(DeleteBehavior.Restrict);
}

public class BranchOptionalUserLoginLogConfiguration : IEntityTypeConfiguration<UserLoginLog>
{
    public void Configure(EntityTypeBuilder<UserLoginLog> builder) =>
        builder.HasOne<Branch>().WithMany().HasForeignKey(e => e.BranchId).OnDelete(DeleteBehavior.Restrict);
}
