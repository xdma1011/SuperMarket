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
    DateTime CreatedAtUtc,
    Guid? DriverId,
    string? DriverName);

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

        var page = orders.Skip(paging.Skip).Take(paging.PageSize);

        var items = await (
            from order in page
            join customer in _context.Customers.AsNoTracking() on order.CustomerId equals customer.Id
            join driver in _context.Users.AsNoTracking() on order.DriverId equals driver.Id into driverJoin
            from driver in driverJoin.DefaultIfEmpty()
            select new OrderListItemDto(
                order.Id,
                order.CustomerId,
                customer.FullName,
                customer.Phone,
                order.BranchId,
                (int)order.Status,
                order.DeliveryNote,
                order.Items.Sum(i => i.Quantity * i.EstimatedUnitPrice),
                order.Items.Count,
                order.CreatedAtUtc,
                order.DriverId,
                driver == null ? null : driver.FullName))
            .ToListAsync(cancellationToken);

        return new PagedResult<OrderListItemDto>(items, totalCount, paging.PageNumber, paging.PageSize);
    }
}
