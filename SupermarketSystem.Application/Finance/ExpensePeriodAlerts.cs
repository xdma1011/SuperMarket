using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Notifications;
using SupermarketSystem.Application.Common.Time;
using SupermarketSystem.Domain.Notifications;

namespace SupermarketSystem.Application.Finance;

/// <summary>
/// تنبيه "مهم" لو فترة مصروف أو راتب (الشهر اللي بينحسب عليه) مش الشهر الحالي ولا اللي قبله، أو بالمستقبل (27/10/2026، ثغرة #7).
/// الفترة حرّة عمدًا (إيجار سنوي، فاتورة متأخرة، راتب متأخر) فما بنمنع (§1.6) - بس نقل مبلغ لشهر بعيد = طريقة لتهريب الربح بين
/// الشهور وأنصبة الشركاء. مشترك بين المصاريف العادية والرواتب (الراتب بينسجّل مصروف بفترته - كان ناقص لحد مراجعة 13/11/2026).
/// </summary>
public static class ExpensePeriodAlerts
{
    public static async Task NotifyIfFarFromEntryAsync(
        IApplicationDbContext context, INotificationDispatcher dispatcher, DateTime utcNow, Guid branchId,
        decimal amount, int periodYear, int periodMonth, string what, string link, CancellationToken cancellationToken)
    {
        var (currentYear, currentMonth) = (await BusinessTime.LoadAsync(context, cancellationToken)).LocalMonth(utcNow);
        var monthsBack = (currentYear * 12 + currentMonth) - (periodYear * 12 + periodMonth);
        if (monthsBack is >= 0 and <= 1)
        {
            return;
        }

        var branchName = await AlertText.BranchNameAsync(context, branchId, cancellationToken);
        await dispatcher.NotifyAsync(
            $"{what} بفترة بعيدة عن تاريخ تسجيله — {amount:0.000} د.أ",
            $"الفرع: {branchName}\nانسجّل بشهر {currentMonth}/{currentYear} على فترة {periodMonth}/{periodYear} " +
            (monthsBack < 0 ? "(بالمستقبل)." : $"(قبل {monthsBack} شهر).") +
            "\nنقل مبلغ لشهر ثاني بيغيّر ربح الشهرين وأنصبة الشركاء — راجعه لو مش مقصود.",
            cancellationToken,
            NotificationSeverity.Warning,
            link: link);
    }
}
