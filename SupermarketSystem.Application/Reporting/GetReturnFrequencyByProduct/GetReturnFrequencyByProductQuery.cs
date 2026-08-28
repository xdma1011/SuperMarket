using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;

namespace SupermarketSystem.Application.Reporting.GetReturnFrequencyByProduct;

public sealed record GetReturnFrequencyByProductQuery(PagedRequest Paging, Guid? BranchId, DateTime FromUtc, DateTime ToUtc);

public sealed record ReturnFrequencyItemDto(
    Guid ProductId,
    string ProductName,
    int ReturnCount,
    decimal TotalQuantityReturned,
    decimal TotalValueReturned);

/// <summary>
/// Architecture Review "Returned Products Review": makes a product with
/// several returns in a short window easy to spot, so an owner can decide
/// whether a surprise stocktake is warranted. This surfaces the pattern —
/// it does not accuse anyone. No score, no flag, no automated conclusion;
/// just counts and totals for a human to look at.
/// </summary>
public sealed class GetReturnFrequencyByProductHandler
{
    private readonly IApplicationDbContext _context;

    public GetReturnFrequencyByProductHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<ReturnFrequencyItemDto>> HandleAsync(
        GetReturnFrequencyByProductQuery query,
        CancellationToken cancellationToken)
    {
        var paging = query.Paging.Normalized();

        var returnItems = _context.ReturnInvoiceItems
            .AsNoTracking()
            .Join(_context.ReturnInvoices.AsNoTracking(),
                item => item.ReturnInvoiceId,
                header => header.Id,
                (item, header) => new { item, header })
            .Where(x => x.header.CreatedAtUtc >= query.FromUtc && x.header.CreatedAtUtc <= query.ToUtc);

        if (query.BranchId is { } branchId)
        {
            returnItems = returnItems.Where(x => x.header.BranchId == branchId);
        }

        var grouped = returnItems
            .GroupBy(x => x.item.ProductId)
            .Select(g => new
            {
                ProductId = g.Key,
                ReturnCount = g.Select(x => x.header.Id).Distinct().Count(),
                TotalQuantityReturned = g.Sum(x => x.item.Quantity),
                TotalValueReturned = g.Sum(x => x.item.LineTotal)
            });

        var totalCount = await grouped.CountAsync(cancellationToken);

        var page = await grouped
            .OrderByDescending(g => g.ReturnCount)
            .ThenByDescending(g => g.TotalValueReturned)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Join(_context.Products.AsNoTracking(),
                g => g.ProductId,
                p => p.Id,
                (g, p) => new ReturnFrequencyItemDto(
                    g.ProductId,
                    p.Name,
                    g.ReturnCount,
                    g.TotalQuantityReturned,
                    g.TotalValueReturned))
            .ToListAsync(cancellationToken);

        return new PagedResult<ReturnFrequencyItemDto>(page, totalCount, paging.PageNumber, paging.PageSize);
    }
}
