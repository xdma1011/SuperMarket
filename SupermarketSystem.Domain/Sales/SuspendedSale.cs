using SupermarketSystem.Domain.Common;

namespace SupermarketSystem.Domain.Sales;

/// <summary>
/// Aggregate root, branch-owned, user-owned. A parked cart — pre-
/// transactional, not yet a completed financial event, so it is NOT subject
/// to the "never delete financial history" rule; it can be deleted once
/// resumed/converted into a real SaleInvoice or abandoned.
/// SuspendedSaleItem was a missing entity in the original list (only the
/// header was named) — added in Architecture Review §4.
/// </summary>
public class SuspendedSale : AuditableEntity, IBranchOwned
{
    public Guid BranchId { get; private set; }
    public Guid UserId { get; private set; }

    private readonly List<SuspendedSaleItem> _items = new();
    public IReadOnlyCollection<SuspendedSaleItem> Items => _items.AsReadOnly();

    private SuspendedSale() { } // EF Core

    public SuspendedSale(Guid branchId, Guid userId)
    {
        BranchId = branchId;
        UserId = userId;
    }

    public SuspendedSaleItem AddItem(Guid productId, Guid productUnitId, decimal quantity, decimal unitPriceSnapshot)
    {
        var item = new SuspendedSaleItem(Id, productId, productUnitId, quantity, unitPriceSnapshot);
        _items.Add(item);
        return item;
    }
}

public class SuspendedSaleItem : Entity
{
    public Guid SuspendedSaleId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid ProductUnitId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal UnitPriceSnapshot { get; private set; }

    private SuspendedSaleItem() { } // EF Core

    internal SuspendedSaleItem(Guid suspendedSaleId, Guid productId, Guid productUnitId, decimal quantity, decimal unitPriceSnapshot)
    {
        SuspendedSaleId = suspendedSaleId;
        ProductId = productId;
        ProductUnitId = productUnitId;
        Quantity = quantity;
        UnitPriceSnapshot = unitPriceSnapshot;
    }
}
