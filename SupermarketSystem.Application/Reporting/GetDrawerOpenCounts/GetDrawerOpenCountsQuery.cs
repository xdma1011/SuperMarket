using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;

namespace SupermarketSystem.Application.Reporting.GetDrawerOpenCounts;

public sealed record GetDrawerOpenCountsQuery(PagedRequest Paging, Guid? BranchId, DateTime FromUtc, DateTime ToUtc);

public sealed record DrawerOpenCountItemDto(
    Guid UserId,
    string Username,
    int OpenCount,
    DateTime FirstOpenedAtUtc,
    DateTime LastOpenedAtUtc);

/// <summary>
/// كم مرة كل كاشير فتح الصندوق بلا بيع بفترة معيّنة (زر "فتح الصندوق" - DrawerOpenEvent). لتقرير
/// "كم مرة باليوم": الفترة يوم واحد (شاشة التقارير بتبعت أيام محلية كاملة). الأكتر فتحًا أولًا.
/// </summary>
public sealed class GetDrawerOpenCountsHandler
{
    private readonly IApplicationDbContext _context;

    public GetDrawerOpenCountsHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<DrawerOpenCountItemDto>> HandleAsync(GetDrawerOpenCountsQuery query, CancellationToken cancellationToken)
    {
        var paging = query.Paging.Normalized();

        var events = _context.DrawerOpenEvents.AsNoTracking()
            .Where(e => e.OccurredAtUtc >= query.FromUtc && e.OccurredAtUtc <= query.ToUtc);

        if (query.BranchId is { } branchId)
        {
            events = events.Where(e => e.BranchId == branchId);
        }

        var grouped = events
            .GroupBy(e => e.UserId)
            .Select(g => new
            {
                UserId = g.Key,
                OpenCount = g.Count(),
                FirstOpenedAtUtc = g.Min(e => e.OccurredAtUtc),
                LastOpenedAtUtc = g.Max(e => e.OccurredAtUtc)
            });

        var totalCount = await grouped.CountAsync(cancellationToken);

        // Left join يدوي - نفس نمط GetCashierVarianceReportQuery: صف بلا مستخدم مُحلّل ما بيختفي.
        var page = await grouped
            .OrderByDescending(g => g.OpenCount)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .GroupJoin(_context.Users.AsNoTracking(),
                g => g.UserId, u => u.Id,
                (g, matchedUsers) => new { g, matchedUsers })
            .SelectMany(
                x => x.matchedUsers.DefaultIfEmpty(),
                (x, u) => new DrawerOpenCountItemDto(
                    x.g.UserId,
                    u != null ? u.Username : "(غير معروف)",
                    x.g.OpenCount,
                    x.g.FirstOpenedAtUtc,
                    x.g.LastOpenedAtUtc))
            .ToListAsync(cancellationToken);

        return new PagedResult<DrawerOpenCountItemDto>(page, totalCount, paging.PageNumber, paging.PageSize);
    }
}
