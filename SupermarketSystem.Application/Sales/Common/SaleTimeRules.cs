namespace SupermarketSystem.Application.Sales.Common;

/// <summary>نتيجة تحديد وقت البيعة: الوقت اللي بينحسب فيه (تقارير، شهر الربح، تكلفة الوحدة، العروض) + تعليم للمراجعة لو لزم.</summary>
public sealed record ResolvedSaleTime(DateTime SaleTimeUtc, string? ReviewFlag);

/// <summary>
/// بند 22 (8/10/2026): وقت البيعة الأوفلاين. كان وقت الفاتورة = وقت وصولها للسيرفر، فبيعة 23:50 آخر يوم بالشهر بتوصل الصبح
/// كانت بتنحسب بالشهر الجاي. الكاشير هلق بيختم كل بيعة بوقت موثوق (ساعة السيرفر المحفوظة محليًا، مش ساعة ويندوز الخام)
/// وبيبعته بـOccurredAtUtc. السيرفر بيقبله بحدود (§1.6: سماح مع تعليم، لا رفض):
///  - بلا وقت (لوحة الإدارة، الطلبات، كاشير قديم) = وقت الوصول، زي قبل.
///  - بالمستقبل أكتر من 5 دقايق = وقت الوصول + تعليم "ساعة الكاشير متقدمة" (ساعة غلط أو محاولة تأخير بيعة لشهر جاي).
///  - أقدم من 7 أيام = وقت الوصول + تعليم (مش منطقي أوفلاين أسبوع؛ ما بنخلّي بيعة ترجع لشهر مقفّل).
///  - أقدم من يوم (لحد 7 أيام) = بينقبل بوقتها + تعليم "وصلت متأخرة" (ممكن تكون سلامة، وممكن تأخير متعمّد حول نهاية الشهر).
/// وقت الوصول بيتحفظ دايمًا بعمود منفصل (SaleInvoice.ReceivedAtUtc)، وسجلات الصندوق والمخزون بتضل بوقت الوصول
/// (ExpectedCash بالتقفيل بيعتمد على ترتيبها الزمني الفعلي).
/// </summary>
public static class SaleTimeRules
{
    public static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);
    public static readonly TimeSpan LateArrivalFlagAfter = TimeSpan.FromDays(1);

    public static ResolvedSaleTime Resolve(DateTime? occurredAtUtc, DateTime arrivalUtc)
    {
        if (occurredAtUtc is not { } claimed)
        {
            return new ResolvedSaleTime(arrivalUtc, null);
        }

        // Kind غير محدد = نعتبره UTC (الكاشير بيبعت ISO بـZ، بس ما بنعتمد).
        claimed = DateTime.SpecifyKind(claimed, DateTimeKind.Utc);

        if (claimed > arrivalUtc + FutureTolerance)
        {
            return new ResolvedSaleTime(arrivalUtc,
                $"وقت البيعة من الكاشير ({claimed:yyyy-MM-dd HH:mm} UTC) بالمستقبل - ساعة الكاشير غلط على الأغلب؛ انحسبت بوقت الوصول.");
        }

        var age = arrivalUtc - claimed;
        if (age > MaxAge)
        {
            return new ResolvedSaleTime(arrivalUtc,
                $"وقت البيعة من الكاشير ({claimed:yyyy-MM-dd HH:mm} UTC) أقدم من {MaxAge.TotalDays:0} أيام - انحسبت بوقت الوصول.");
        }

        if (age > LateArrivalFlagAfter)
        {
            return new ResolvedSaleTime(claimed,
                $"البيعة وصلت متأخرة {age.TotalHours:0} ساعة عن وقتها ({claimed:yyyy-MM-dd HH:mm} UTC) - انحسبت بوقتها الأصلي.");
        }

        return new ResolvedSaleTime(claimed > arrivalUtc ? arrivalUtc : claimed, null);
    }
}
