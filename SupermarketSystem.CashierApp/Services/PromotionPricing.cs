using SupermarketSystem.CashierApp.Local;

namespace SupermarketSystem.CashierApp.Services;

/// <summary>
/// نسخة حرفية من PromotionPricingCalculator بالباك إند (Application/Common/Promotions) - لازم تضل مطابقة بالزبط:
/// الكاشير بيحسب المبلغ اللي الزبون بيدفعه، والسيرفر بيحسب الفاتورة بنفس المعادلة (OFFLINE PROMOTION بـ
/// CompleteSaleHandler)، وأي فرق = الدفعة ما بتطابق والبيعة بتعلق بالطابور. الكاشير ما بيرجع للـApplication.
/// </summary>
public static class PromotionPricing
{
    public static decimal PromotionAmount(
        decimal quantity, decimal regularUnitPrice, int bundleQuantity, decimal bundlePrice, decimal? maxQuantityPerInvoice)
    {
        if (bundleQuantity <= 0 || quantity <= 0)
        {
            return 0m;
        }

        var eligibleQuantity = maxQuantityPerInvoice is { } max ? Math.Min(quantity, max) : quantity;
        var fullBundles = Math.Floor(eligibleQuantity / bundleQuantity);
        if (fullBundles <= 0)
        {
            return 0m;
        }

        var promoQuantity = fullBundles * bundleQuantity;
        var promotionAmount = promoQuantity * regularUnitPrice - fullBundles * bundlePrice;
        return promotionAmount > 0 ? promotionAmount : 0m;
    }

    /// <summary>العرض الشغّال هلق (حسب ساعة الجهاز) للصنف - لو أكتر من واحد، الأقدم إنشاءً (نفس أولوية السيرفر).</summary>
    public static LocalPromotion? ActiveFor(LocalDbContext db, Guid productId, DateTime nowUtc) =>
        db.Promotions
            .Where(p => p.ProductId == productId)
            .AsEnumerable()
            .Where(p => p.StartAtUtc <= nowUtc && p.EndAtUtc >= nowUtc)
            .OrderBy(p => p.CreatedAtUtc)
            .FirstOrDefault();
}
