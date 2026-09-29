using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Costing;

namespace SupermarketSystem.Application.Sales.Common;

/// <summary>
/// تكلفة الوحدة الأساسية وقت البيع - نفس منطق UnitCostSnapshot بالضبط (راجع PROFIT ASSUMPTION
/// بـCompleteSaleHandler): منتج بدفعات = ProductBatch.UnitCost، غير هيك = متوسط مرجّح من فواتير
/// الشراء المستلمة لحد هاللحظة. منتج بلا تاريخ شراء = غايب من القاموس (مش صفر).
/// مشترك بين البيع العادي وعرض سعر السحب بسعر التكلفة، عشان الرقمين ما يختلفوا أبدًا.
/// </summary>
public static class SaleUnitCosts
{
    public static async Task<(Dictionary<Guid, decimal> BatchCosts, Dictionary<Guid, decimal> AverageCosts)> LoadAsync(
        IApplicationDbContext context,
        IReadOnlyCollection<(Guid ProductId, Guid? ProductBatchId)> lines,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var batchIds = lines.Where(l => l.ProductBatchId is not null)
            .Select(l => l.ProductBatchId!.Value).Distinct().ToList();
        var batchCosts = batchIds.Count == 0
            ? new Dictionary<Guid, decimal>()
            : await context.ProductBatches.AsNoTracking()
                .Where(b => batchIds.Contains(b.Id))
                .Select(b => new { b.Id, b.UnitCost })
                .ToDictionaryAsync(b => b.Id, b => b.UnitCost, cancellationToken);

        var nonBatchProductIds = lines.Where(l => l.ProductBatchId is null)
            .Select(l => l.ProductId).Distinct().ToList();
        // بالوحدة الأساسية (كرتونة = 10 حبات) - راجع PurchaseCostBasis.
        var averageCosts = await PurchaseCostBasis.AverageBaseUnitCostsAsync(
            context, nonBatchProductIds, nowUtc, inclusive: true, cancellationToken);

        return (batchCosts, averageCosts);
    }

    /// <summary>سعر السحب بسعر التكلفة لوحدة البيع - مقرّب لأقرب فلس (3 خانات) عشان المبلغ اللي بيندفع يطابق الفاتورة بالضبط.</summary>
    public static decimal AtCostUnitPrice(decimal baseUnitCost, decimal conversionFactorToBase) =>
        Math.Round(baseUnitCost * conversionFactorToBase, 3, MidpointRounding.AwayFromZero);
}
