using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;

namespace SupermarketSystem.Application.CashManagement.GetCashClosings;

public sealed record GetCashClosingsQuery(PagedRequest Paging, Guid? BranchId);

public sealed record CashClosingListItemDto(
    Guid Id,
    Guid BranchId,
    string BranchName,
    DateOnly BusinessDate,
    DateTime ClosedAtUtc,
    decimal ExpectedCash,
    decimal CountedCash,
    decimal Variance);

/// <summary>كانت مفقودة - CompleteCashClosing موجود بلا أي طريقة لعرض قائمة التقفيلات السابقة.</summary>
public sealed class GetCashClosingsHandler
{
    private readonly IApplicationDbContext _context;

    public GetCashClosingsHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<CashClosingListItemDto>> HandleAsync(GetCashClosingsQuery query, CancellationToken cancellationToken)
    {
        var paging = query.Paging.Normalized();

        var closings = _context.CashClosings.AsNoTracking().AsQueryable();

        if (query.BranchId is not null)
        {
            closings = closings.Where(c => c.BranchId == query.BranchId.Value);
        }

        closings = paging.IsDescending
            ? closings.OrderByDescending(c => c.BusinessDate).ThenByDescending(c => c.Id)
            : closings.OrderBy(c => c.BusinessDate).ThenBy(c => c.Id);

        var totalCount = await closings.CountAsync(cancellationToken);

        var rows = await closings
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Select(c => new
            {
                c.Id,
                c.BranchId,
                c.BusinessDate,
                c.ClosedAtUtc,
                c.ExpectedCash,
                c.CountedCash,
                c.Variance
            })
            .ToListAsync(cancellationToken);

        var branchNames = await _context.Branches.AsNoTracking()
            .Where(b => rows.Select(r => r.BranchId).Contains(b.Id))
            .Select(b => new { b.Id, b.Name })
            .ToDictionaryAsync(b => b.Id, b => b.Name, cancellationToken);

        var items = rows
            .Select(r => new CashClosingListItemDto(
                r.Id,
                r.BranchId,
                branchNames.GetValueOrDefault(r.BranchId, "(غير معروف)"),
                r.BusinessDate,
                r.ClosedAtUtc,
                r.ExpectedCash,
                r.CountedCash,
                r.Variance))
            .ToList();

        return new PagedResult<CashClosingListItemDto>(items, totalCount, paging.PageNumber, paging.PageSize);
    }
}
