namespace SupermarketSystem.Application.Common.Interfaces;

/// <summary>
/// مفاتيح إعدادات المخزون (Inventory). منفصلة عن PosPolicyKeys لأنها ليست
/// قرار "مسموح/ممنوع" لعملية POS محددة (زي الإرجاع أو الخصم اليدوي)، هي
/// إعداد سلوكي عام على مستوى المخزون نفسه — بيأثر على *طريقة* تنفيذ عملية
/// موجودة أصلًا (خصم المخزون)، مش على *إمكانية* تنفيذها.
/// </summary>
public static class InventorySettingsKeys
{
    /// <summary>
    /// لو "true" (الافتراضي): البيع بيكمل دايمًا حتى لو المخزون بالنظام
    /// غير كافٍ، والرصيد بينزل لتحت الصفر فعليًا. السبب: أحيانًا البضاعة
    /// تكون وصلت فعليًا من المندوب والفاتورة لسه ما دخلت عالنظام (بانتظار
    /// اعتماد) — رفض البيع بهاي الحالة معناه خسارة زبون واقف قدامك
    /// بالمنتج فعليًا. القيمة السالبة بتضل مرئية بتقرير مخصص للمراجعة.
    ///
    /// لو "false": يرجع المنع الأصلي (Architecture Review §12) — البيع
    /// يُرفض لو المخزون غير كافٍ.
    /// </summary>
    public const string AllowNegativeStock = "Inventory.AllowNegativeStock";

    /// <summary>عدد الأيام قبل انتهاء الصلاحية اللي تبدأ فيها دفعة تظهر بتقرير "قرب انتهاء الصلاحية" (GetExpiringBatchesQuery). افتراضي 14 يوم.</summary>
    public const string ExpiryAlertThresholdDays = "Inventory.ExpiryAlertThresholdDays";

    /// <summary>
    /// قيمة فائض الجرد (بالتكلفة، دينار) اللي فوقها تنبيه الفائض بيصير "خطير" بدل "مهم" (27/10/2026). الفائض بيزيد الربح وأنصبة الشركاء،
    /// فمساعد أدمن ممكن يعدّ بزيادة ويعتمد - مهما كان صغير بنبّه، وفوق هالحد بنصعّد. 0 = ما بنصعّد أبدًا. الافتراضي 100.
    /// </summary>
    public const string StocktakeSurplusCriticalValue = "Stocktake.SurplusCriticalValue";

    public const decimal DefaultStocktakeSurplusCriticalValue = 100m;
}
