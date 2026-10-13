namespace SupermarketSystem.CashierApp.Views;

public sealed class CartLine
{
    public Guid ProductId { get; set; }
    public Guid ProductUnitId { get; set; }
    public Guid? ProductBatchId { get; set; }
    public string? BatchNumber { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string UnitName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }

    /// <summary>معامل تحويل وحدة السطر للوحدة الأساسية (كرتونة 12 = 12، الحبة = 1) - العرض بيتحسب بالوحدة الأساسية (PROMOTION UNITS بالسيرفر). الافتراضي 1 لفواتير معلّقة قديمة.</summary>
    public decimal ConversionFactorToBase { get; set; } = 1m;
    public decimal LineTotal => Quantity * UnitPrice - PromotionAmount;

    /// <summary>
    /// عرض الكمية الشغّال لحظة إضافة الصنف (29/9/2026) - بيتحسب بالوحدة الأساسية (كمية × معامل التحويل) زي السيرفر. الحساب بنفس معادلة
    /// السيرفر (PromotionPricing)، والسطر بيبعت PromotionId بس لو العرض انطبق فعلًا (الكمية بتكفي حزمة) - السيرفر
    /// بيحترم هالقرار (OFFLINE PROMOTION)، فالمبلغ اللي دفعه الزبون بيطابق الفاتورة.
    /// </summary>
    public Guid? PromotionId { get; set; }
    public string? PromotionTitle { get; set; }
    public int PromotionBundleQuantity { get; set; }
    public decimal PromotionBundlePrice { get; set; }
    public decimal? PromotionMaxQuantity { get; set; }

    public decimal PromotionAmount => PromotionId is null
        ? 0m
        : Services.PromotionPricing.PromotionAmount(
            Quantity * ConversionFactorToBase, ConversionFactorToBase > 0 ? UnitPrice / ConversionFactorToBase : UnitPrice,
            PromotionBundleQuantity, PromotionBundlePrice, PromotionMaxQuantity);

    public Guid? AppliedPromotionId => PromotionAmount > 0 ? PromotionId : null;

    public string PromotionMark => PromotionAmount > 0
        ? $"🏷 {PromotionTitle} (−{PromotionAmount:0.000})"
        : PromotionId is not null ? $"🏷 {PromotionTitle}" : "";

    /// <summary>
    /// رقم نسخة الكتالوج المحلية اللي انقرأ منها UnitPrice - بينبعت مع السطر، والسيرفر بيحسب
    /// السطر بسعر هالنسخة بالضبط (راجع OFFLINE PRICE بـCompleteSaleHandler). لكل سطر لحاله
    /// لأن المزامنة الخلفية ممكن تحدّث الأسعار وأنت بنص السلة.
    /// </summary>
    public long? CatalogVersion { get; set; }

    /// <summary>
    /// الكمية المطلوبة تجاوزت الرصيد المحلي المخزَّن آخر مزامنة لهذه
    /// الدفعة - سماح مع مراجعة (CLAUDE.md §1.6)، لا منع: السيرفر هو
    /// الحكم الفعلي (AllowNegativeStock + SucceededWentNegative)، هذا
    /// مجرد تنبيه بصري للكاشير إنه احتمال البضاعة وصلت فعليًا ولسه ما
    /// انزامنت محليًا.
    /// </summary>
    public bool NeedsReview { get; set; }
    public string ReviewMark => NeedsReview ? "⚠ مراجعة" : "";
}
