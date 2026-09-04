using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Domain.Ordering;

namespace SupermarketSystem.Application.Ordering.GetPendingOrders;

public sealed record GetPendingOrdersQuery(PagedRequest Paging, Guid? BranchId, OrderStatus? Status);

public sealed record OrderListItemDto(
    Guid Id,
    Guid CustomerId,
    string CustomerName,
    string? CustomerPhone,
    Guid BranchId,
    int Status,
    string? DeliveryNote,
    decimal EstimatedTotal,
    int ItemCount,
    DateTime CreatedAtUtc);

public sealed class GetPendingOrdersHandler
{
    private readonly IApplicationDbContext _context;

    public GetPendingOrdersHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<OrderListItemDto>> HandleAsync(GetPendingOrdersQuery query, CancellationToken cancellationToken)
    {
        var paging = query.Paging.Normalized();

        var orders = _context.Orders.AsNoTracking().AsQueryable();

        if (query.BranchId is { } branchId)
        {
            orders = orders.Where(o => o.BranchId == branchId);
        }

        orders = query.Status is { } status
            ? orders.Where(o => o.Status == status)
            : orders.Where(o => o.Status == OrderStatus.Pending || o.Status == OrderStatus.Accepted);

        orders = orders.OrderByDescending(o => o.CreatedAtUtc).ThenByDescending(o => o.Id);

        var totalCount = await orders.CountAsync(cancellationToken);

        var items = await orders
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Join(_context.Customers.AsNoTracking(), o => o.CustomerId, c => c.Id,
                (o, c) => new
                {
                    Order = o,
                    CustomerName = c.FullName,
                    CustomerPhone = c.Phone
                })
            .Select(x => new OrderListItemDto(
                x.Order.Id,
                x.Order.CustomerId,
                x.CustomerName,
                x.CustomerPhone,
                x.Order.BranchId,
                (int)x.Order.Status,
                x.Order.DeliveryNote,
                x.Order.Items.Sum(i => i.Quantity * i.EstimatedUnitPrice),
                x.Order.Items.Count,
                x.Order.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return new PagedResult<OrderListItemDto>(items, totalCount, paging.PageNumber, paging.PageSize);
    }
}
