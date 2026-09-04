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
}
