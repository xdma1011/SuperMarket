using SupermarketSystem.Domain.Common;

namespace SupermarketSystem.Domain.Inventory;

/// <summary>
/// Aggregate root. The transactionally-maintained current-balance
/// projection off StockMovement — what "how much is in stock right now"
/// queries hit, so that check never means summing the entire movement
/// history (Architecture Review §12). Added in Architecture Review Phase B
/// as a missing entity from the original list.
///
/// Deliberately NOT foreign-keyed to ProductBranch (see ProductBranch.cs
/// remarks) — Stock references Product and Branch directly.
///
/// IMPORTANT: the methods below (Increase/Decrease) are for the normal
/// EF-tracked path (e.g. receiving a purchase, where overselling isn't a
/// concern). The hot-path SALE decrement does NOT go through a loaded
/// entity + SaveChanges — it uses a raw, atomic conditional
/// `UPDATE ... SET QuantityOnHand = QuantityOnHand - @qty
///  WHERE ... AND QuantityOnHand >= @qty`
/// issued directly against the table (Infrastructure concern), because that
/// atomicity is exactly what prevents two concurrent sales from overselling
/// the same product — an entity-then-SaveChanges round trip has a race
/// window that raw conditional SQL does not.
/// </summary>
public class Stock : Entity, IBranchOwned, IHasRowVersion
{
    public Guid ProductId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid? ProductBatchId { get; private set; }
    public decimal QuantityOnHand { get; private set; }
    public byte[]? RowVersion { get; private set; }

    private Stock() { } // EF Core

    public Stock(Guid productId, Guid branchId, Guid? productBatchId)
    {
        ProductId = productId;
        BranchId = branchId;
        ProductBatchId = productBatchId;
        QuantityOnHand = 0;
    }

    public void Increase(decimal quantity)
    {
        if (quantity <= 0)
        {
            throw new DomainException("Increase quantity must be positive.");
        }

        QuantityOnHand += quantity;
    }

    /// <summary>
    /// Non-hot-path decrement (e.g. a stocktake correction applied directly
    /// to an already-loaded aggregate). Guards against negative stock in
    /// memory; the sale-completion hot path uses the raw atomic SQL
    /// decrement described in the class remarks instead of this method.
    /// </summary>
    public void Decrease(decimal quantity)
    {
        if (quantity <= 0)
        {
            throw new DomainException("Decrease quantity must be positive.");
        }

        if (QuantityOnHand - quantity < 0)
        {
            throw new DomainException("This operation would result in negative stock, which is disallowed by default policy.");
        }

        QuantityOnHand -= quantity;
    }
}
