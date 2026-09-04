using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Policies;
using SupermarketSystem.Application.Inventory.RecordComplimentaryIssue;

namespace SupermarketSystem.Application.System.GetAdminSettings;

public enum AdminSettingDataType
{
    Boolean = 1,
    Decimal = 2,
    String = 3
}

public sealed record AdminSettingDto(string Key, string Label, string Value, AdminSettingDataType DataType);

public sealed record GetAdminSettingsResponse(IReadOnlyList<AdminSettingDto> Settings);

/// <summary>
/// طول العملية اليومية الحسّاسة (إلغاء بيع، إرجاع، خصم يدوي، ضيافة...)
/// قابلة للتشغيل/الإيقاف من صفحة إعدادات واحدة بدل تعديل مباشر بقاعدة
/// البيانات. القائمة هون *محدودة عمدًا* (whitelist صريحة) - أسرار زي
/// مفاتيح Gemini/Claude/Telegram Bot Token متعمَّد استبعادها من هالصفحة،
/// لأنها تحتاج تعامل مختلف (إخفاء/Masking) لا نص عادي بالواجهة.
/// </summary>
public sealed class GetAdminSettingsHandler
{
    // (المفتاح، تسمية عربية للواجهة، نوع القيمة) - كل إعداد الصفحة هاي بتديره.
    internal static readonly (string Key, string Label, AdminSettingDataType DataType)[] ManagedSettings =
    {
        (PosPolicyKeys.AllowVoidSale, "السماح بإلغاء فاتورة بيع مكتملة", AdminSettingDataType.Boolean),
        (PosPolicyKeys.AllowReturn, "السماح بمعالجة إرجاع من زبون", AdminSettingDataType.Boolean),
        (PosPolicyKeys.AllowCrossMethodRefund, "السماح بالاسترجاع بطريقة دفع مختلفة عن الأصلية", AdminSettingDataType.Boolean),
        (PosPolicyKeys.AllowManualDiscount, "السماح بخصم يدوي عند البيع", AdminSettingDataType.Boolean),
        (PosPolicyKeys.MaxManualDiscountPercentage, "أقصى نسبة خصم يدوي مسموحة (%) - 0 يعطّل الخصم اليدوي كليًا", AdminSettingDataType.Decimal),
        (PosPolicyKeys.AllowPaymentReversal, "السماح بعكس دفعة مكتملة", AdminSettingDataType.Boolean),
        (PosPolicyKeys.HighValueReturnThreshold, "حد قيمة الإرجاع المرتفعة - يُعلَّم للمراجعة فوقه (0 = تعطيل)", AdminSettingDataType.Decimal),
        (InventorySettingsKeys.AllowNegativeStock, "السماح بالبيع رغم نقص المخزون بالنظام", AdminSettingDataType.Boolean),
        (ComplimentarySettingsKeys.DailyReviewThresholdQuantity, "الحد اليومي لكمية الضيافة قبل التعليم للمراجعة", AdminSettingDataType.Decimal),
        // معرَّف بـInfrastructure.Services.PendingReviewSettingsKeys - Application
        // ما بيقدر يعتمد على Infrastructure (اتجاه الاعتمادية)، فالمفتاح
        // مكرَّر هون كنص صريح عمدًا، لا استيراد.
        ("PendingReview.EscalationThresholdDays", "عدد الأيام قبل تصعيد عنصر بانتظار المراجعة", AdminSettingDataType.Decimal),
        (PurchasingPolicyKeys.PriceIncreaseWarningThresholdPercent, "نسبة ارتفاع سعر الشراء (%) قبل التعليم للمراجعة (مقارنة بمتوسط آخر 5 عمليات شراء)", AdminSettingDataType.Decimal),
        (OrderingPolicyKeys.Enabled, "استقبال طلبات تطبيق الزبائن مفعَّل", AdminSettingDataType.Boolean),
        (OrderingPolicyKeys.MinimumOrderAmount, "أقل مبلغ إجمالي مسموح للطلب (0 = بلا حد أدنى)", AdminSettingDataType.Decimal),
        (OrderingPolicyKeys.LoyaltyEnabled, "إظهار وتفعيل نقاط الولاء بتطبيق الزبائن", AdminSettingDataType.Boolean),
        (OrderingPolicyKeys.LoyaltyPointsPerCurrencyUnit, "عدد نقاط الولاء لكل دينار من قيمة الطلب المكتمل (0 = بلا اكتساب)", AdminSettingDataType.Decimal),
        (OrderingPolicyKeys.VisibilityHighPriceThreshold, "الحد الفاصل الأعلى للسعر (دينار) لقاعدة إخفاء المخزون القليل بتطبيق الزبائن", AdminSettingDataType.Decimal),
        (OrderingPolicyKeys.VisibilityLowPriceThreshold, "الحد الفاصل الأدنى للسعر (دينار) لقاعدة إخفاء المخزون القليل بتطبيق الزبائن", AdminSettingDataType.Decimal),
        (OrderingPolicyKeys.MinVisibleStockHighPrice, "أقل كمية مخزون لإظهار منتج سعره أعلى من الحد الفاصل الأعلى", AdminSettingDataType.Decimal),
        (OrderingPolicyKeys.MinVisibleStockMidPrice, "أقل كمية مخزون لإظهار منتج سعره بين الحدّين", AdminSettingDataType.Decimal),
        (OrderingPolicyKeys.MinVisibleStockLowPrice, "أقل كمية مخزون لإظهار منتج سعره أقل من الحد الفاصل الأدنى", AdminSettingDataType.Decimal),
        (TelegramSettingsKeys.BotUsername, "اسم مستخدم بوت تلغرام (بدون @، للرابط العلني)", AdminSettingDataType.String),
        (OrderingPolicyKeys.DailyOrderCountAlertThreshold, "عدد طلبات نفس الزبون باليوم قبل تنبيهك (إساءة استخدام محتملة)", AdminSettingDataType.Decimal)
    };

    private readonly IApplicationDbContext _context;

    public GetAdminSettingsHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<GetAdminSettingsResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var keys = ManagedSettings.Select(m => m.Key).ToList();

        var values = await _context.SystemSettings.AsNoTracking()
            .Where(s => keys.Contains(s.Key))
            .Select(s => new { s.Key, s.Value })
            .ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);

        var settings = ManagedSettings
            .Select(m => new AdminSettingDto(m.Key, m.Label, values.GetValueOrDefault(m.Key, string.Empty), m.DataType))
            .ToList();

        return new GetAdminSettingsResponse(settings);
    }
}
