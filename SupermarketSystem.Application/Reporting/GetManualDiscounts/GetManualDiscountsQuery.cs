using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;

namespace SupermarketSystem.Application.Reporting.GetManualDiscounts;

/// <summary>
/// Level distinguishes a per-line manual discount from a whole-order one;
/// ProductId is null for Invoice-level rows. CashierUserId is nullable for
/// the same reason as GetRecentReturns.RecentReturnItemDto — CreatedByUserId
/// is currently always null under PlaceholderCurrentUserContext, and a
/// discount record must remain visible in this report regardless.
/// </summary>
public sealed record ManualDiscountItemDto(
    string Level,
    Guid SaleInvoiceId,
    string InvoiceNumber,
    Guid BranchId,
    Guid? ProductId,
    decimal DiscountAmount,
    Guid? CashierUserId,
    DateTime CreatedAtUtc);

public sealed record GetManualDiscountsQuery(PagedRequest Paging, Guid? BranchId, DateTime? FromUtc, DateTime? ToUtc);

/// <summary>
/// Architecture Review §16.10/§13.6: a manual discount IS a discount snapshot
/// with DiscountId == NULL — there is no separate stored flag, that absence
/// is the signal. This query is exactly what makes that absence queryable at
/// both the line level (a single item discounted at checkout) and the
/// invoice level (a whole-order discount), unioned into one management view.
/// </summary>
public sealed class GetManualDiscountsHandler
{
    private readonly IApplicationDbContext _context;

    public GetManualDiscountsHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<ManualDiscountItemDto>> HandleAsync(
        GetManualDiscountsQuery query,
        CancellationToken cancellationToken)
    {
        var paging = query.Paging.Normalized();

        var invoices = _context.SaleInvoices.AsNoTracking().AsQueryable();

        if (query.BranchId is { } branchId)
        {
            invoices = invoices.Where(s => s.BranchId == branchId);
        }

        if (query.FromUtc is { } fromUtc)
        {
            invoices = invoices.Where(s => s.CreatedAtUtc >= fromUtc);
        }

        if (query.ToUtc is { } toUtc)
        {
            invoices = invoices.Where(s => s.CreatedAtUtc <= toUtc);
        }

        var lineLevel = _context.SaleInvoiceItems
            .AsNoTracking()
            .Where(i => i.DiscountId == null && i.DiscountSnapshot > 0)
            .Join(invoices, i => i.SaleInvoiceId, s => s.Id, (i, s) => new ManualDiscountItemDto(
                "Line",
                s.Id,
                s.InvoiceNumber,
                s.BranchId,
                i.ProductId,
                i.DiscountSnapshot,
                s.CreatedByUserId,
                s.CreatedAtUtc));

        var invoiceLevel = invoices
            .Where(s => s.DiscountId == null && s.DiscountAmountSnapshot > 0)
            .Select(s => new ManualDiscountItemDto(
                "Invoice",
                s.Id,
                s.InvoiceNumber,
                s.BranchId,
                null,
                s.DiscountAmountSnapshot,
                s.CreatedByUserId,
                s.CreatedAtUtc));

        // EF Core translates Concat to UNION ALL on SQL Server, so ordering
        // and paging below still happen database-side over the combined set.
        var combined = lineLevel.Concat(invoiceLevel);

        var totalCount = await combined.CountAsync(cancellationToken);

        var items = await combined
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<ManualDiscountItemDto>(items, totalCount, paging.PageNumber, paging.PageSize);
    }
}
