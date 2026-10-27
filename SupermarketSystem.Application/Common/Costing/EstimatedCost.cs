using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;

namespace SupermarketSystem.Application.Common.Costing;

/// <summary>
/// مفتاح إعداد التكلفة التقديرية (27/10/2026، قرار صاحب المشروع: "الأصناف اللي بدون سعر تكلفة خليها 15% وخليني أغيرها من صفحة النظام").
/// </summary>
public static class CostingSettingsKeys
{
    /// <summary>
    /// هامش الربح التقديري (%) للصنف اللي ما إله تكلفة معروفة (ما إله فاتورة شراء ولا رصيد افتتاحي): التكلفة التقديرية = سعر البيع × (1 − الهامش).
    /// الافتراضي 15 (يعني تكلفة = 85% من سعر البيع). 0 = تعطيل: الصنف بلا تكلفة بيتستبعد من الربح زي قبل (null مش صفر).
    /// </summary>
    public const string EstimatedMarginPercent = "Costing.EstimatedMarginPercent";

    public const decimal DefaultEstimatedMarginPercent = 15m;

    /// <summary>سقف الهامش المقبول - فوقه التكلفة التقديرية بتقترب من صفر وبتنفخ الربح (نفس خطر "تكلفة صفر").</summary>
    public const decimal MaxEstimatedMarginPercent = 90m;
}

/// <summary>
/// التكلفة التقديرية لصنف بلا تكلفة معروفة. مبدأ §1.6: بنحسب ونعلّم، ما بنستثني ولا بنوقف. كل مكان بيستعمل تكلفة تقديرية لازم
/// يعلّمها (IsCostEstimated بسطر البيع، عدّاد التقدير بكشف الربح) - الرقم تقدير مش حقيقة، والكشف بيحذّر منه.
/// </summary>
public static class EstimatedCost
{
    /// <summary>الهامش الفعّال من الإعدادات: قيمة خارج [0, 90] أو غير صالحة = الافتراضي/السقف (إعداد غلط ما بيكسر ولا بيزوّر الأرقام).</summary>
    public static async Task<decimal> LoadMarginPercentAsync(ISettingsProvider settings, CancellationToken cancellationToken) =>
        Normalize(await settings.GetDecimalAsync(
            CostingSettingsKeys.EstimatedMarginPercent, CostingSettingsKeys.DefaultEstimatedMarginPercent, cancellationToken));

    /// <summary>نفس القراءة مباشرة من الجدول (بلا كاش) - للكشوف/التقارير اللي ما عندها ISettingsProvider ونادرة الاستدعاء.</summary>
    public static async Task<decimal> LoadMarginPercentAsync(IApplicationDbContext context, CancellationToken cancellationToken)
    {
        var raw = await context.SystemSettings.AsNoTracking()
            .Where(x => x.Key == CostingSettingsKeys.EstimatedMarginPercent)
            .Select(x => x.Value)
            .FirstOrDefaultAsync(cancellationToken);
        return decimal.TryParse(raw, global::System.Globalization.NumberStyles.Number, global::System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? Normalize(parsed)
            : CostingSettingsKeys.DefaultEstimatedMarginPercent;
    }

    public static decimal Normalize(decimal marginPercent) =>
        marginPercent < 0 ? 0m : Math.Min(marginPercent, CostingSettingsKeys.MaxEstimatedMarginPercent);

    /// <summary>تكلفة الوحدة الأساسية التقديرية من سعر بيعها الأساسي، أو null لو التقدير معطّل (هامش 0) أو السعر غير صالح.</summary>
    public static decimal? FromSellingPrice(decimal baseSellingPrice, decimal marginPercent)
    {
        if (marginPercent <= 0 || baseSellingPrice <= 0)
        {
            return null;
        }

        return Math.Round(baseSellingPrice * (1m - marginPercent / 100m), 4, MidpointRounding.AwayFromZero);
    }
}
