namespace SupermarketSystem.Application.Common.Policies;

/// <summary>مفتاح إعداد نسبة تحذير ارتفاع سعر الشراء - نفس نمط PosPolicyKeys (مفتاح ثابت مركزي، لا نص سحري متكرر).</summary>
public static class PurchasingPolicyKeys
{
    /// <summary>
    /// النسبة المئوية اللي فوقها سطر شراء يُعلَّم "بانتظار مراجعة" - لو
    /// السعر الجديد أعلى من متوسط آخر 5 عمليات شراء لنفس المنتج بأكتر من
    /// هالنسبة. "سماح مع مراجعة" - لا يوقف الفاتورة أبدًا، بس يعلّم
    /// السطر ليظهر بقائمة المراجعات الموحَّدة (CLAUDE.md §1.6).
    /// </summary>
    public const string PriceIncreaseWarningThresholdPercent = "Purchasing.PriceIncreaseWarningThresholdPercent";
}
