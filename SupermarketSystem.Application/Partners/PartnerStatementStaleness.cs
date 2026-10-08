using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Notifications;
using SupermarketSystem.Application.Common.Time;
using SupermarketSystem.Domain.Notifications;

namespace SupermarketSystem.Application.Partners;

/// <summary>
/// كشف الشركاء "قديم" (المراجعة النقدية 6/10/2026، بند 2): لما ينزل كشف شهر وبعدها ينضاف شي بفترته (مصروف، راتب، حركة رأس مال رجعية،
/// إرجاع/إلغاء فاتورة من هالشهر) صافي الربح بيتغيّر والكشف بيضل على الرقم القديم، والشركاء ممكن يسحبوا على رقم غلط.
/// بنعلّم الكشف + تنبيه "مهم" مرة لكل كشف - سماح مع تعليم، مش منع (§1.6). إعادة الإصدار بتصفّر العلامة.
///
/// "أفضل جهد": فشل التعليم ما بيفشّل العملية الأصلية (اللي التزمت أصلًا). بيشتغل بعد حفظ العملية.
/// </summary>
public static class PartnerStatementStaleness
{
    /// <summary>الكشوف اللي بتتأثر بحركة رأس مال بتاريخ معيّن: كل كشف شهره انتهى بعد هالتاريخ (رأس المال بيتحسب آخر الشهر).</summary>
    public static async Task<IReadOnlyList<(int Year, int Month)>> MonthsAffectedByCapitalAsync(
        IApplicationDbContext context, Guid branchId, DateTime occurredAtUtc, CancellationToken cancellationToken)
    {
        var months = await context.PartnerMonthlyStatements.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.BranchId == branchId)
            .Select(s => new { s.Year, s.Month })
            .ToListAsync(cancellationToken);
        if (months.Count == 0)
        {
            return Array.Empty<(int, int)>();
        }

        var businessTime = await BusinessTime.LoadAsync(context, cancellationToken);
        return months
            .Where(m => businessTime.MonthRangeUtc(m.Year, m.Month).EndUtc > occurredAtUtc)
            .Select(m => (m.Year, m.Month))
            .ToList();
    }

    /// <summary>شهر (سنة، شهر) بتوقيت المحل لتاريخ فاتورة - لإرجاع/إلغاء فاتورة قديمة.</summary>
    public static async Task<(int Year, int Month)> LocalMonthAsync(
        IApplicationDbContext context, DateTime utc, CancellationToken cancellationToken) =>
        (await BusinessTime.LoadAsync(context, cancellationToken)).LocalMonth(utc);

    public static async Task MarkAndNotifyAsync(
        IApplicationDbContext context, INotificationDispatcher dispatcher, DateTime utcNow, Guid branchId,
        IEnumerable<(int Year, int Month)> months, string reason, CancellationToken cancellationToken)
    {
        try
        {
            var marked = new List<(int Year, int Month)>();
            foreach (var (year, month) in months.Distinct())
            {
                var statement = await context.PartnerMonthlyStatements.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(s => s.BranchId == branchId && s.Year == year && s.Month == month, cancellationToken);
                if (statement is not null && statement.MarkStale(utcNow, reason))
                {
                    marked.Add((year, month));
                }
            }

            if (marked.Count == 0)
            {
                return;
            }

            await context.SaveChangesAsync(cancellationToken);

            var branchName = await AlertText.BranchNameAsync(context, branchId, cancellationToken);
            foreach (var (year, month) in marked)
            {
                await dispatcher.NotifyAsync(
                    $"كشف الشركاء {month}/{year} صار قديم — أعد إصداره",
                    $"الفرع: {branchName}\nالسبب: {reason}\n" +
                    "الأنصبة المسجَّلة بالكشف ما عادت تطابق الربح الفعلي للشهر. أعد إصدار الكشف من صفحة الشركاء قبل ما حدا يسحب.",
                    cancellationToken,
                    NotificationSeverity.Warning,
                    link: "/partners?tab=statements");
            }
        }
        catch (Exception)
        {
            // أفضل جهد: العملية الأصلية التزمت، وفشل التعليم ما لازم يرجّع خطأ للمستخدم.
        }
    }
}
