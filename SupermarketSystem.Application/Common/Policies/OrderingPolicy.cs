namespace SupermarketSystem.Application.Common.Policies;

/// <summary>مفاتيح إعدادات تطبيق الزبائن - نفس نمط PosPolicyKeys/PurchasingPolicyKeys.</summary>
public static class OrderingPolicyKeys
{
    /// <summary>إيقاف/تشغيل استقبال طلبات جديدة بالكامل من عندك (صاحب المشروع) - PlaceOrderHandler بيرفض أي طلب جديد لو false.</summary>
    public const string Enabled = "Ordering.Enabled";

    /// <summary>أقل مبلغ إجمالي تقديري مسموح بيه الطلب - 0 يعطّل الحد الأدنى كليًا.</summary>
    public const string MinimumOrderAmount = "Ordering.MinimumOrderAmount";

    /// <summary>تشغيل/إيقاف ظهور صفحة نقاط الولاء بتطبيق الزبائن بالكامل - مخفية تمامًا لحد ما تُفعَّل (راجع نقاش صاحب المشروع).</summary>
    public const string LoyaltyEnabled = "Loyalty.Enabled";

    /// <summary>عدد طلبات نفس الزبون بنفس اليوم اللي فوقه يُرسَل تنبيه لصاحب المشروع - مؤشر إساءة استخدام محتملة، لا يمنع الطلب.</summary>
    public const string DailyOrderCountAlertThreshold = "Ordering.DailyOrderCountAlertThreshold";

    /// <summary>عدد نقاط الولاء المكتسبة لكل وحدة عملة من إجمالي الطلب المكتمل - 0 يعطّل الاكتساب كليًا حتى لو LoyaltyEnabled=true.</summary>
    public const string LoyaltyPointsPerCurrencyUnit = "Loyalty.PointsPerCurrencyUnit";

    // === حد أدنى للمخزون قبل ظهور المنتج بتطبيق الزبائن (راجع نقاش صاحب
    // المشروع - منتج بمخزون قليل جدًا ما لازم يظهر أصلًا، وإلا زبون
    // يطلبه والكمية الفعلية تنخلص قبل ما توصل الفاتورة الحقيقية للكاشير).
    // ثلاث فئات سعرية، كل فئة إلها حد أدنى مختلف - كلما رخص الصنف، كلما
    // زاد الحد الأدنى المطلوب (منتج بنص دينار المفروض عنده كمية أكبر
    // بطبيعته من منتج بعشر دنانير).

    /// <summary>الحد الفاصل الأعلى للسعر (دينار) - فوقه تُطبَّق MinVisibleStockHighPrice.</summary>
    public const string VisibilityHighPriceThreshold = "Ordering.VisibilityHighPriceThreshold";

    /// <summary>الحد الفاصل الأدنى للسعر (دينار) - تحته تُطبَّق MinVisibleStockLowPrice، بينهم MinVisibleStockMidPrice.</summary>
    public const string VisibilityLowPriceThreshold = "Ordering.VisibilityLowPriceThreshold";

    /// <summary>الحد الأدنى للمخزون ليظهر منتج سعره أعلى من VisibilityHighPriceThreshold.</summary>
    public const string MinVisibleStockHighPrice = "Ordering.MinVisibleStockHighPrice";

    /// <summary>الحد الأدنى للمخزون ليظهر منتج سعره بين الحدين.</summary>
    public const string MinVisibleStockMidPrice = "Ordering.MinVisibleStockMidPrice";

    /// <summary>الحد الأدنى للمخزون ليظهر منتج سعره أقل من VisibilityLowPriceThreshold.</summary>
    public const string MinVisibleStockLowPrice = "Ordering.MinVisibleStockLowPrice";
}
