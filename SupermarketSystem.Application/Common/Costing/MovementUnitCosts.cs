using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;

namespace SupermarketSystem.Application.Common.Costing;

/// <summary>
/// تكلفة الحبة لحركات مخزون بفترة (تلف، ضيافة، فروقات جرد) - مصدر وحيد لكشف الربح الشهري وملخّص الضيافة
/// (30/9/2026)، عشان الرقمين يطلعوا نفس الشي دايمًا: حركة من دفعة = تكلفة الدفعة؛ غيرها = متوسط مرجّح من المشتريات
/// لحد نهاية الفترة (PurchaseCostBasis). منتج بلا تاريخ شراء: لو فيه هامش تقديري (27/10/2026، EstimatedCost) = تكلفة تقديرية
/// من سعر بيعه الحالي بالفرع ومعلَّمة (IsEstimated)؛ بدونه (هامش 0 أو بلا سعر) null = بيتستبعد صراحة، مش بتكلفة صفر.
/// </summary>
public static class MovementUnitCosts
{
    public sealed record Resolver(Func<Guid, Guid?, decimal?> UnitCostOf, Func<Guid, Guid?, bool> IsEstimated);

    public static async Task<Func<Guid, Guid?, decimal?>> LoadAsync(
        IApplicationDbContext context, IReadOnlyCollection<(Guid ProductId, Guid? ProductBatchId)> movements,
        DateTime periodEndUtc, CancellationToken cancellationToken,
        Guid? branchId = null, decimal estimatedMarginPercent = 0m) =>
        (await LoadWithEstimatesAsync(context, movements, periodEndUtc, cancellationToken, branchId, estimatedMarginPercent)).UnitCostOf;

    public static async Task<Resolver> LoadWithEstimatesAsync(
        IApplicationDbContext context, IReadOnlyCollection<(Guid ProductId, Guid? ProductBatchId)> movements,
        DateTime periodEndUtc, CancellationToken cancellationToken,
        Guid? branchId = null, decimal estimatedMarginPercent = 0m)
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

        // تقدير للأصناف (غير الدفعات) بلا متوسط مرجّح فقط، من سعر بيعها الحالي بالفرع - رقم محسوب لحظيًا زي باقي هالحركات، مش مجمَّد.
        var estimatedCosts = new Dictionary<Guid, decimal>();
        var margin = EstimatedCost.Normalize(estimatedMarginPercent);
        if (branchId is { } estimateBranchId && margin > 0)
        {
            var uncostedProductIds = nonBatchProductIds.Where(id => !weightedAverageCosts.ContainsKey(id)).ToList();
            if (uncostedProductIds.Count > 0)
            {
                var prices = await context.ProductBranches.IgnoreQueryFilters().AsNoTracking()
                    .Where(pb => pb.BranchId == estimateBranchId && uncostedProductIds.Contains(pb.ProductId))
                    .Select(pb => new { pb.ProductId, pb.SellingPrice })
                    .ToListAsync(cancellationToken);
                foreach (var price in prices)
                {
                    if (EstimatedCost.FromSellingPrice(price.SellingPrice, margin) is { } estimated)
                    {
                        estimatedCosts[price.ProductId] = estimated;
                    }
                }
            }
        }

        return new Resolver(
            (productId, batchId) => batchId is { } id
                ? (batchUnitCosts.TryGetValue(id, out var batchCost) ? batchCost : null)
                : (weightedAverageCosts.TryGetValue(productId, out var avgCost) ? avgCost
                    : estimatedCosts.TryGetValue(productId, out var estimatedCost) ? estimatedCost : null),
            (productId, batchId) => batchId is null
                && !weightedAverageCosts.ContainsKey(productId) && estimatedCosts.ContainsKey(productId));
    }
}
