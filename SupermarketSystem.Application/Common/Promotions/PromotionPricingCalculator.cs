namespace SupermarketSystem.Application.Common.Promotions;

/// <summary>
/// دالة رياضية بحتة (بلا EF/DB) - محسوبة هون بمكان وحيد عشان تُستخدم
/// بالضبط بنفس الحساب بكل مكان يحتاج "شو السعر لو في عرض" (CompleteSaleCommand
/// الجهة الوحيدة الرسمية/الملزِمة، وأي معاينة سعر تقديرية بكتالوج
/// الزبون/الكاشير الأوفلاين لاحقًا لو احتجناها) - تفادي حسابين مختلفين
/// بمكانين يطلعوا نتيجة مختلفة لنفس المدخلات.
/// </summary>
public static class PromotionPricingCalculator
{
    public readonly record struct Outcome(decimal PromotionAmount, decimal PromoQuantityApplied);

    /// <summary>
    /// quantity: الكمية المطلوبة بالسطر. regularUnitPrice: سعر الحبة العادي
    /// (من ProductBranch.SellingPrice). bundleQuantity/bundlePrice: تعريف
    /// العرض (N بسعر كذا). maxQuantityPerInvoice: سقف اختياري على كمية
    /// السطر المؤهَّلة للتسعير بالعرض - أي زيادة عنه تُسعَّر عاديًا.
    ///
    /// المنطق: عدد الحزم الكاملة = floor(الكمية المؤهَّلة ÷ N)، والباقي
    /// (أقل من N، أو أي كمية تجاوزت السقف) يُسعَّر بالسعر العادي حبة حبة.
    /// </summary>
    public static Outcome Calculate(
        decimal quantity, decimal regularUnitPrice, int bundleQuantity, decimal bundlePrice, decimal? maxQuantityPerInvoice)
    {
        if (bundleQuantity <= 0 || quantity <= 0)
        {
            return new Outcome(0m, 0m);
        }

        var eligibleQuantity = maxQuantityPerInvoice is { } max ? Math.Min(quantity, max) : quantity;
        var fullBundles = Math.Floor(eligibleQuantity / bundleQuantity);

        if (fullBundles <= 0)
        {
            return new Outcome(0m, 0m);
        }

        var promoQuantity = fullBundles * bundleQuantity;
        var regularPriceForPromoQuantity = promoQuantity * regularUnitPrice;
        var promotionAmount = regularPriceForPromoQuantity - (fullBundles * bundlePrice);

        // لو سعر الحزمة المُدخَل أعلى من السعر العادي (خطأ إدخال إداري)،
        // ما نعطي "خصم سالب" (يعني نزيد السعر) - نتجاهل العرض لهالسطر
        // بهدوء بدل ما نكسر الفاتورة بسعر أعلى من العادي بلا تفسير للزبون.
        return promotionAmount > 0 ? new Outcome(promotionAmount, promoQuantity) : new Outcome(0m, 0m);
    }
}
