using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;

namespace SupermarketSystem.Application.Common.Costing;

/// <summary>
/// تكلفة الحبة لحركات مخزون بفترة (تلف، ضيافة، فروقات جرد) - مصدر وحيد لكشف الربح الشهري وملخّص الضيافة
/// (30/9/2026)، عشان الرقمين يطلعوا نفس الشي دايمًا: حركة من دفعة = تكلفة الدفعة؛ غيرها = متوسط مرجّح من المشتريات
/// لحد نهاية الفترة (PurchaseCostBasis). null = منتج بلا تاريخ شراء - بيتستبعد صراحة، مش بتكلفة صفر.
/// </summary>
public static class MovementUnitCosts
{
    public static async Task<Func<Guid, Guid?, decimal?>> LoadAsync(
        IApplicationDbContext context, IReadOnlyCollection<(Guid ProductId, Guid? ProductBatchId)> movements,
        DateTime periodEndUtc, CancellationToken cancellationToken)
    {
        var batchIds = movements.Where(m => m.ProductBatchId is not null).Select(m => m.ProductBatchId!.Value).Distinct().ToList();
        var batchUnitCosts = batchIds.Count == 0
            ? new Dictionary<Guid, decimal>()
            : await context.ProductBatches.AsNoTracking()
                .Where(b => batchIds.Contains(b.Id))
                .Select(b => new { b.Id, b.UnitCost })
                .ToDictionaryAsync(b => b.Id, b => b.UnitCost, cancellationToken);

        var nonBatchProductIds = movements.Where(m => m.ProductBatchId is null).Select(m => m.ProductId).Distinct().ToList();
        var weightedAverageCosts = await PurchaseCostBasis.AverageBaseUnitCostsAsync(
            context, nonBatchProductIds, periodEndUtc, inclusive: false, cancellationToken);

        return (productId, batchId) => batchId is { } id
            ? (batchUnitCosts.TryGetValue(id, out var batchCost) ? batchCost : null)
            : (weightedAverageCosts.TryGetValue(productId, out var avgCost) ? avgCost : null);
    }
}
