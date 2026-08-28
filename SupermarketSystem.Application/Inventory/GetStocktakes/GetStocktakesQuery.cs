using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Domain.Inventory;

namespace SupermarketSystem.Application.Inventory.GetStocktakes;

public sealed record GetStocktakesQuery(PagedRequest Paging, Guid? BranchId);

public sealed record StocktakeListItemDto(
    Guid StocktakeId,
    string StocktakeNumber,
    Guid BranchId,
    string BranchName,
    int StatusCode,
    string StatusTitle,
    int ItemCount,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc,
    DateTime? ApprovedAtUtc);

/// <summary>
/// كانت ناقصة بالكامل — GetStocktakeById موجود من قبل (لعرض جرد واحد
/// بمعرّفه)، بس صفر طريقة تشوف "كل عمليات الجرد" لتختار منها.
/// </summary>
public sealed class GetStocktakesHandler
{
    private readonly IApplicationDbContext _context;

    public GetStocktakesHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<StocktakeListItemDto>> HandleAsync(GetStocktakesQuery query, CancellationToken cancellationToken)
    {
        var paging = query.Paging.Normalized();

        var stocktakesQuery = _context.Stocktakes.AsNoTracking()
            .Where(s => query.BranchId == null || s.BranchId == query.BranchId);

        var totalCount = await stocktakesQuery.CountAsync(cancellationToken);

        var page = await stocktakesQuery
            .OrderByDescending(s => s.CreatedAtUtc)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Select(s => new
            {
                s.Id,
                s.StocktakeNumber,
                s.BranchId,
                s.Status,
                s.CreatedAtUtc,
                s.CompletedAtUtc,
                s.ApprovedAtUtc
            })
            .ToListAsync(cancellationToken);

        var stocktakeIds = page.Select(x => x.Id).ToList();

        // عدّ منفصل بدل s.Items.Count داخل Select — IReadOnlyCollection
        // (لا ICollection مباشرة) ما لها ترجمة SQL مضمونة بكل حالات
        // EF Core، فاستعلام GroupBy صريح على الجدول مباشرة أضمن وأوضح.
        var itemCounts = await _context.StocktakeItems.AsNoTracking()
            .Where(i => stocktakeIds.Contains(i.StocktakeId))
            .GroupBy(i => i.StocktakeId)
            .Select(g => new { StocktakeId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.StocktakeId, x => x.Count, cancellationToken);

        var branchIds = page.Select(x => x.BranchId).Distinct().ToList();
        var branchNames = await _context.Branches.AsNoTracking()
            .Where(b => branchIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, b => b.Name, cancellationToken);

        var items = page.Select(x => new StocktakeListItemDto(
            x.Id, x.StocktakeNumber, x.BranchId, branchNames.GetValueOrDefault(x.BranchId, "—"),
            (int)x.Status, StatusTitle(x.Status), itemCounts.GetValueOrDefault(x.Id, 0),
            x.CreatedAtUtc, x.CompletedAtUtc, x.ApprovedAtUtc))
            .ToList();

        return new PagedResult<StocktakeListItemDto>(items, totalCount, paging.PageNumber, paging.PageSize);
    }

    private static string StatusTitle(StocktakeStatus status) => status switch
    {
        StocktakeStatus.Draft => "مسوَّدة",
        StocktakeStatus.InProgress => "جارٍ العدّ",
        StocktakeStatus.Completed => "مكتمل - بانتظار الاعتماد",
        StocktakeStatus.Approved => "معتمَد",
        StocktakeStatus.Cancelled => "ملغى",
        _ => status.ToString()
    };
}
