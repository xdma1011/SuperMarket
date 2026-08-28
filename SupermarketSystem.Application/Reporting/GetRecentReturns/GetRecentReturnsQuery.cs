using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Domain.Sales;

namespace SupermarketSystem.Application.Reporting.GetRecentReturns;

public sealed record GetRecentReturnsQuery(PagedRequest Paging, Guid? BranchId, DateTime? FromUtc, DateTime? ToUtc);

/// <summary>
/// CashierUserId/CashierUsername are nullable — NOT because the business
/// concept is optional, but because until real authentication replaces
/// PlaceholderCurrentUserContext, every row's CreatedByUserId is stamped
/// null (the audit interceptor has no real user to attribute it to). A
/// return record must still be visible in this report even when who
/// recorded it cannot yet be resolved to a display name — hiding the row
/// entirely would be worse than showing it with an unresolved cashier.
/// </summary>
public sealed record RecentReturnItemDto(
    Guid ReturnInvoiceId,
    string InvoiceNumber,
    Guid BranchId,
    Guid OriginalSaleInvoiceId,
    Guid? CashierUserId,
    string CashierUsername,
    ReturnReason Reason,
    decimal TotalAmount,
    decimal TotalRefundedAmount,
    DateTime CreatedAtUtc);

/// <summary>
/// Architecture Review §14: "Recently returned invoices" — a pure read-model
/// query over transactional data, no stored entity. The
/// (BranchId, CreatedAtUtc) index on ReturnInvoice is what keeps this cheap
/// at volume.
/// </summary>
public sealed class GetRecentReturnsHandler
{
    private const string UnresolvedCashierLabel = "(unresolved)";

    private readonly IApplicationDbContext _context;

    public GetRecentReturnsHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<RecentReturnItemDto>> HandleAsync(
        GetRecentReturnsQuery query,
        CancellationToken cancellationToken)
    {
        var paging = query.Paging.Normalized();

        var returns = _context.ReturnInvoices.AsNoTracking().AsQueryable();

        if (query.BranchId is { } branchId)
        {
            returns = returns.Where(r => r.BranchId == branchId);
        }

        if (query.FromUtc is { } fromUtc)
        {
            returns = returns.Where(r => r.CreatedAtUtc >= fromUtc);
        }

        if (query.ToUtc is { } toUtc)
        {
            returns = returns.Where(r => r.CreatedAtUtc <= toUtc);
        }

        returns = returns.OrderByDescending(r => r.CreatedAtUtc).ThenByDescending(r => r.Id);

        var totalCount = await returns.CountAsync(cancellationToken);

        // Left join, not inner: a return whose CreatedByUserId cannot be
        // resolved to a User row (currently always true — see the
        // CashierUserId remarks) must still appear in this report.
        var items = await returns
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .GroupJoin(_context.Users.AsNoTracking(),
                r => r.CreatedByUserId,
                u => (Guid?)u.Id,
                (r, matchedUsers) => new { r, matchedUsers })
            .SelectMany(
                x => x.matchedUsers.DefaultIfEmpty(),
                (x, u) => new RecentReturnItemDto(
                    x.r.Id,
                    x.r.InvoiceNumber,
                    x.r.BranchId,
                    x.r.OriginalSaleInvoiceId,
                    x.r.CreatedByUserId,
                    u != null ? u.Username : UnresolvedCashierLabel,
                    x.r.Reason,
                    x.r.TotalAmount,
                    x.r.TotalRefundedAmount,
                    x.r.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return new PagedResult<RecentReturnItemDto>(items, totalCount, paging.PageNumber, paging.PageSize);
    }
}
