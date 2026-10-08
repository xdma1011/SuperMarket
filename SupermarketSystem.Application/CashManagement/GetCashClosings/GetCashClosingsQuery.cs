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
    int ShiftNumber,
    DateTime ClosedAtUtc,
    decimal ExpectedCash,
    decimal CountedCash,
    decimal Variance,
    // بند 23: بيعات كانت بطابور الكاشير لحظة التقفيل، والمفسَّر من الفرق (ملاحظات صاحب المحل)، وغير المفسَّر (بنفس إشارة الفرق).
    int PendingSalesCount = 0,
    decimal PendingSalesAmount = 0m,
    decimal ExplainedVariance = 0m,
    decimal UnexplainedVariance = 0m);

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
            ? closings.OrderByDescending(c => c.BusinessDate).ThenByDescending(c => c.ShiftNumber).ThenByDescending(c => c.Id)
            : closings.OrderBy(c => c.BusinessDate).ThenBy(c => c.ShiftNumber).ThenBy(c => c.Id);

        var totalCount = await closings.CountAsync(cancellationToken);

        var rows = await closings
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Select(c => new
            {
                c.Id,
                c.BranchId,
                c.BusinessDate,
                c.ShiftNumber,
                c.ClosedAtUtc,
                c.ExpectedCash,
                c.CountedCash,
                c.Variance,
                c.PendingSalesCount,
                c.PendingSalesAmount
            })
            .ToListAsync(cancellationToken);

        var branchNames = await _context.Branches.AsNoTracking()
            .Where(b => rows.Select(r => r.BranchId).Contains(b.Id))
            .Select(b => new { b.Id, b.Name })
            .ToDictionaryAsync(b => b.Id, b => b.Name, cancellationToken);

        var rowIds = rows.Select(r => r.Id).ToList();
        var explainedByClosing = await _context.CashClosingVarianceNotes.AsNoTracking()
            .Where(n => rowIds.Contains(n.CashClosingId))
            .GroupBy(n => n.CashClosingId)
            .Select(g => new { ClosingId = g.Key, Total = g.Sum(n => n.ExplainedAmount) })
            .ToDictionaryAsync(x => x.ClosingId, x => x.Total, cancellationToken);

        var items = rows
            .Select(r => new CashClosingListItemDto(
                r.Id,
                r.BranchId,
                branchNames.GetValueOrDefault(r.BranchId, "(غير معروف)"),
                r.BusinessDate,
                r.ShiftNumber,
                r.ClosedAtUtc,
                r.ExpectedCash,
                r.CountedCash,
                r.Variance,
                r.PendingSalesCount,
                r.PendingSalesAmount,
                explainedByClosing.GetValueOrDefault(r.Id),
                VarianceNotes.VarianceNoteMath.Unexplained(r.Variance, explainedByClosing.GetValueOrDefault(r.Id))))
            .ToList();

        return new PagedResult<CashClosingListItemDto>(items, totalCount, paging.PageNumber, paging.PageSize);
    }
}
