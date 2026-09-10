using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Domain.Catalog;

namespace SupermarketSystem.Application.Catalog.GetPendingPriceChangeRequests;

public sealed record GetPendingPriceChangeRequestsQuery;

public sealed record PriceChangeRequestListItemDto(
    Guid Id,
    Guid ProductBranchId,
    Guid ProductId,
    string ProductName,
    string BranchName,
    decimal PreviousPrice,
    decimal RequestedPrice,
    string RequestedByUserName,
    DateTime RequestedAtUtc);

/// <summary>قائمة الطلبات بانتظار الموافقة - صفحة مراجعة المدير (Catalog.ChangePriceDirect).</summary>
public sealed class GetPendingPriceChangeRequestsHandler
{
    private readonly IApplicationDbContext _context;

    public GetPendingPriceChangeRequestsHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<PriceChangeRequestListItemDto>> HandleAsync(
        GetPendingPriceChangeRequestsQuery query, CancellationToken cancellationToken)
    {
        var rows = await _context.PriceChangeRequests.AsNoTracking()
            .Where(r => r.Status == PriceChangeRequestStatus.Pending)
            .OrderBy(r => r.RequestedAtUtc)
            .Join(_context.ProductBranches.AsNoTracking(), r => r.ProductBranchId, pb => pb.Id,
                (r, pb) => new { r.Id, r.ProductBranchId, pb.ProductId, pb.BranchId, r.PreviousPrice, r.RequestedPrice, r.RequestedByUserId, r.RequestedAtUtc })
            .Join(_context.Products.AsNoTracking(), x => x.ProductId, p => p.Id, (x, p) => new { x, ProductName = p.Name })
            .Join(_context.Branches.AsNoTracking(), x => x.x.BranchId, b => b.Id, (x, b) => new
            {
                x.x.Id,
                x.x.ProductBranchId,
                x.x.ProductId,
                x.ProductName,
                BranchName = b.Name,
                x.x.PreviousPrice,
                x.x.RequestedPrice,
                x.x.RequestedByUserId,
                x.x.RequestedAtUtc
            })
            .ToListAsync(cancellationToken);

        var userIds = rows.Select(r => r.RequestedByUserId).Distinct().ToList();
        var userNames = await _context.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName })
            .ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);

        return rows
            .Select(r => new PriceChangeRequestListItemDto(
                r.Id, r.ProductBranchId, r.ProductId, r.ProductName, r.BranchName,
                r.PreviousPrice, r.RequestedPrice,
                userNames.GetValueOrDefault(r.RequestedByUserId, "(غير معروف)"),
                r.RequestedAtUtc))
            .ToList();
    }
}
