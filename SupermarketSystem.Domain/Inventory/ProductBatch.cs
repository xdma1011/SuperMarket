using SupermarketSystem.Domain.Common;

namespace SupermarketSystem.Domain.Inventory;

/// <summary>
/// Aggregate root, branch-owned. Deliberately NOT a child of Product (see
/// Product.cs) — batches belong to the Inventory bounded context, keyed by
/// where the physical stock actually is. Only created for products where
/// Product.IsBatchTracked is true; not every product needs a batch.
/// </summary>
public class ProductBatch : AuditableEntity, IBranchOwned
{
    public Guid ProductId { get; private set; }
    public Guid BranchId { get; private set; }
    public string BatchNumber { get; private set; } = null!;
    public DateOnly? ExpiryDate { get; private set; }
    public decimal UnitCost { get; private set; }

    private ProductBatch() { } // EF Core

    public ProductBatch(Guid productId, Guid branchId, string batchNumber, DateOnly? expiryDate, decimal unitCost)
    {
        ProductId = productId;
        BranchId = branchId;
        BatchNumber = batchNumber;
        ExpiryDate = expiryDate;
        UnitCost = unitCost;
    }
}
