using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Domain.Purchasing;

namespace SupermarketSystem.Application.Common.Costing;

/// <summary>سطر شراء مستلم محوّل للوحدة الأساسية - أساس كل متوسط مرجّح للتكلفة.</summary>
public sealed class BasePurchaseLine
{
    public Guid ProductId { get; init; }
    public Guid BranchId { get; init; }
    public DateTime PurchasedAtUtc { get; init; }

    /// <summary>الكمية بالوحدة الأساسية (كمية السطر × معامل وحدة الشراء).</summary>
    public decimal BaseQuantity { get; init; }

    /// <summary>قيمة السطر (كمية × سعر وحدة الشراء) - نفس المبلغ بأي وحدة.</summary>
    public decimal TotalCost { get; init; }
}

/// <summary>
/// إصلاح (29/9/2026، انمسك باختبار "شهر كامل بالمحل"): كل حساب متوسط مرجّح كان ياخد كمية وسعر سطر الشراء كأنهم
/// بالحبة - شراء 4 كراتين × 8.000 كان يطلع "تكلفة الحبة 8.000" بدل 0.800، فكل بيعة من منتج انشرى بالكرتونة كانت
/// تسجّل تكلفة ×10 (كشف ربح بخسارة وهمية، قيمة مخزون منفوخة، وتوزيع أرباح شركاء غلط). هون المصدر الوحيد: الكمية
/// بتتحوّل للوحدة الأساسية، والتكلفة = مجموع قيم الأسطر ÷ مجموع الكميات الأساسية.
/// </summary>
public static class PurchaseCostBasis
{
    /// <summary>أسطر فواتير الشراء المستلمة (Received) بالوحدة الأساسية.</summary>
    public static IQueryable<BasePurchaseLine> ReceivedLines(IApplicationDbContext context) =>
        from item in context.PurchaseInvoiceItems.AsNoTracking()
        join invoice in context.PurchaseInvoices.AsNoTracking() on item.PurchaseInvoiceId equals invoice.Id
        join unit in context.ProductUnits.AsNoTracking() on item.ProductUnitId equals unit.Id
        where invoice.Status == PurchaseInvoiceStatus.Received
        select new BasePurchaseLine
        {
            ProductId = item.ProductId,
            BranchId = invoice.BranchId,
            PurchasedAtUtc = invoice.CreatedAtUtc,
            BaseQuantity = item.Quantity * unit.ConversionFactorToBase,
            TotalCost = item.Quantity * item.UnitCost
        };

    /// <summary>
    /// متوسط تكلفة الحبة (الوحدة الأساسية) لكل منتج من المشتريات المستلمة لحد وقت معيّن (شامل أو حصري).
    /// منتج بلا أي شراء قبل هالوقت = مش موجود بالقاموس (تكلفة غير معروفة، مش صفر).
    /// </summary>
    public static async Task<Dictionary<Guid, decimal>> AverageBaseUnitCostsAsync(
        IApplicationDbContext context, IReadOnlyCollection<Guid> productIds, DateTime untilUtc, bool inclusive,
        CancellationToken cancellationToken)
    {
        if (productIds.Count == 0)
        {
            return new Dictionary<Guid, decimal>();
        }

        var lines = ReceivedLines(context).Where(l => productIds.Contains(l.ProductId));
        lines = inclusive ? lines.Where(l => l.PurchasedAtUtc <= untilUtc) : lines.Where(l => l.PurchasedAtUtc < untilUtc);

        var totals = await lines
            .GroupBy(l => l.ProductId)
            .Select(g => new { ProductId = g.Key, BaseQuantity = g.Sum(l => l.BaseQuantity), TotalCost = g.Sum(l => l.TotalCost) })
            .ToListAsync(cancellationToken);

        return totals.Where(t => t.BaseQuantity > 0).ToDictionary(t => t.ProductId, t => t.TotalCost / t.BaseQuantity);
    }

    /// <summary>سعر وحدة شراء محوّل لتكلفة الحبة (لتكلفة الدفعة والمقارنات بين وحدات مختلفة).</summary>
    public static decimal ToBaseUnitCost(decimal purchaseUnitCost, decimal conversionFactorToBase) =>
        conversionFactorToBase > 0 ? purchaseUnitCost / conversionFactorToBase : purchaseUnitCost;
}
