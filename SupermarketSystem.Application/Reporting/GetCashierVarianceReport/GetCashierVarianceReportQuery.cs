using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;

namespace SupermarketSystem.Application.Reporting.GetCashierVarianceReport;

public sealed record GetCashierVarianceReportQuery(PagedRequest Paging, Guid? BranchId, DateTime FromUtc, DateTime ToUtc);

public sealed record CashierVarianceItemDto(
    Guid UserId,
    string Username,
    int ClosingsCount,
    int DeficitCount,
    int SurplusCount,
    decimal TotalVariance,
    decimal AverageVariance,
    // بند 23: مجموع الفرق اللي لسه بلا تفسير من صاحب المحل (مجموع غير المفسَّر لكل تقفيل بإشارته: سالب = عجز بلا تفسير).
    decimal UnexplainedVariance = 0m);

/// <summary>
/// فروقات تقفيل الصندوق (`CashClosing.Variance`) مجمَّعة لكل كاشير عبر
/// فترة - الهدف كشف نمط عجز متكرر (لا حادثة واحدة معزولة، الفرق هون هو
/// تكرار `DeficitCount` عبر عدة تقفيلات مختلفة). `TotalVariance` سالب =
/// عجز إجمالي، موجب = زيادة إجمالية. مرتَّب تصاعديًا حسب `TotalVariance`
/// (الأكتر عجزًا أولًا - نفس فلسفة GetNegativeStock بترتيب الأولوية).
/// </summary>
public sealed class GetCashierVarianceReportHandler
{
    private readonly IApplicationDbContext _context;

    public GetCashierVarianceReportHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<CashierVarianceItemDto>> HandleAsync(
        GetCashierVarianceReportQuery query, CancellationToken cancellationToken)
    {
        var paging = query.Paging.Normalized();

        var closings = _context.CashClosings.AsNoTracking()
            .Where(c => c.ClosedAtUtc >= query.FromUtc && c.ClosedAtUtc <= query.ToUtc);

        if (query.BranchId is { } branchId)
        {
            closings = closings.Where(c => c.BranchId == branchId);
        }

        var grouped = closings
            .GroupBy(c => c.UserId)
            .Select(g => new
            {
                UserId = g.Key,
                ClosingsCount = g.Count(),
                DeficitCount = g.Count(c => c.CountedCash < c.ExpectedCash),
                SurplusCount = g.Count(c => c.CountedCash > c.ExpectedCash),
                TotalVariance = g.Sum(c => c.CountedCash - c.ExpectedCash),
                AverageVariance = g.Average(c => c.CountedCash - c.ExpectedCash)
            });

        var totalCount = await grouped.CountAsync(cancellationToken);

        // Left join يدوي - نفس نمط GetBestCashiersQuery بالضبط: صف بلا
        // مستخدم مُحلّل ما لازم يختفي من التقرير.
        var page = await grouped
            .OrderBy(g => g.TotalVariance)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .GroupJoin(_context.Users.AsNoTracking(),
                g => g.UserId, u => u.Id,
                (g, matchedUsers) => new { g, matchedUsers })
            .SelectMany(
                x => x.matchedUsers.DefaultIfEmpty(),
                (x, u) => new CashierVarianceItemDto(
                    x.g.UserId,
                    u != null ? u.Username : "(غير معروف)",
                    x.g.ClosingsCount,
                    x.g.DeficitCount,
                    x.g.SurplusCount,
                    x.g.TotalVariance,
                    x.g.AverageVariance))
            .ToListAsync(cancellationToken);

        // غير المفسَّر لكل كاشير بالصفحة: من تقفيلاته بالفترة ناقص ملاحظات التفسير (بند 23). عدد التقفيلات محدود (وردية يوميًا).
        var pageUserIds = page.Select(p => p.UserId).ToList();
        var pageClosings = await closings
            .Where(c => pageUserIds.Contains(c.UserId))
            .Select(c => new { c.Id, c.UserId, Variance = c.CountedCash - c.ExpectedCash })
            .ToListAsync(cancellationToken);
        var pageClosingIds = pageClosings.Select(c => c.Id).ToList();
        var explained = await _context.CashClosingVarianceNotes.AsNoTracking()
            .Where(n => pageClosingIds.Contains(n.CashClosingId))
            .GroupBy(n => n.CashClosingId)
            .Select(g => new { ClosingId = g.Key, Total = g.Sum(n => n.ExplainedAmount) })
            .ToDictionaryAsync(x => x.ClosingId, x => x.Total, cancellationToken);
        var unexplainedByUser = pageClosings
            .GroupBy(c => c.UserId)
            .ToDictionary(g => g.Key, g => g.Sum(c => CashManagement.VarianceNotes.VarianceNoteMath.Unexplained(c.Variance, explained.GetValueOrDefault(c.Id))));
        page = page.Select(p => p with { UnexplainedVariance = unexplainedByUser.GetValueOrDefault(p.UserId) }).ToList();

        return new PagedResult<CashierVarianceItemDto>(page, totalCount, paging.PageNumber, paging.PageSize);
    }
}
