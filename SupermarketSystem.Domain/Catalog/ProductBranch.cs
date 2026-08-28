using SupermarketSystem.Domain.Common;

namespace SupermarketSystem.Domain.Catalog;

/// <summary>
/// Independent aggregate root — NOT a child of Product, and NOT merged with
/// Stock, despite all three keying off (ProductId, BranchId). See
/// Architecture Review v2 §1/§3/§10 for the full reasoning; short version:
///
///   - Product stays global/catalog-only, so editing a product's name never
///     touches per-branch pricing rows.
///   - ProductBranch is edited rarely (a manager changes a price/threshold);
///     Stock is written on nearly every sale. Merging them would make a
///     routine price edit contend, via RowVersion, with live POS traffic on
///     the same row.
///   - No FK exists from Stock to ProductBranch (see Stock.cs) — stock can
///     legitimately exist before a branch has finalized pricing.
///
/// A product is not sellable at a branch until this row exists — there is
/// no implicit fallback to Product.SuggestedRetailPrice.
/// </summary>
public class ProductBranch : AuditableEntity, IBranchOwned, IHasRowVersion
{
    public Guid ProductId { get; private set; }
    public Guid BranchId { get; private set; }
    public decimal SellingPrice { get; private set; }
    public decimal? MinimumStock { get; private set; }
    public decimal? MaximumStock { get; private set; }
    public bool IsAvailableForSale { get; private set; }
    public byte[]? RowVersion { get; private set; }

    private ProductBranch() { } // EF Core

    public ProductBranch(Guid productId, Guid branchId, decimal sellingPrice)
    {
        if (sellingPrice < 0)
        {
            throw new DomainException("Selling price cannot be negative.");
        }

        ProductId = productId;
        BranchId = branchId;
        SellingPrice = sellingPrice;
        IsAvailableForSale = true;
    }

    public void ChangePrice(decimal newPrice)
    {
        if (newPrice < 0)
        {
            throw new DomainException("Selling price cannot be negative.");
        }

        SellingPrice = newPrice;
    }

    public void SetStockThresholds(decimal? minimumStock, decimal? maximumStock)
    {
        if (minimumStock is not null && maximumStock is not null && minimumStock > maximumStock)
        {
            throw new DomainException("Minimum stock cannot exceed maximum stock.");
        }

        MinimumStock = minimumStock;
        MaximumStock = maximumStock;
    }

    public void MakeAvailable() => IsAvailableForSale = true;
    public void MakeUnavailable() => IsAvailableForSale = false;
}
