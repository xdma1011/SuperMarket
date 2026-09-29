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
    string? DriverName,
    string? CouponCode = null,
    decimal EstimatedCouponDiscount = 0m);

/// <summary>كوبون كل طلب بالقائمة (29/9/2026) - الخصم الفعلي بعد التسليم أو التقديري قبله؛ المرجوع مع الرفض ما بيبين.</summary>
internal static class OrderCouponInfo
{
    public static async Task<List<OrderListItemDto>> AttachAsync(
        IApplicationDbContext context, List<OrderListItemDto> items, CancellationToken cancellationToken)
    {
        var orderIds = items.Select(i => i.Id).ToList();
        if (orderIds.Count == 0)
        {
            return items;
        }

        var coupons = await (
            from r in context.CouponRedemptions.AsNoTracking()
            join c in context.Coupons.AsNoTracking() on r.CouponId equals c.Id
            where orderIds.Contains(r.OrderId) && r.Status != CouponRedemptionStatus.Released
            select new { r.OrderId, c.Code, Discount = r.DiscountAmount ?? r.EstimatedDiscountAmount })
            .ToDictionaryAsync(x => x.OrderId, cancellationToken);

        return items
            .Select(i => coupons.TryGetValue(i.Id, out var c) ? i with { CouponCode = c.Code, EstimatedCouponDiscount = c.Discount } : i)
            .ToList();
    }
}

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
        items = await OrderCouponInfo.AttachAsync(_context, items, cancellationToken);

        return new PagedResult<OrderListItemDto>(items, totalCount, paging.PageNumber, paging.PageSize);
    }
}
