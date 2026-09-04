using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Application.Ordering.GetPendingOrders;

namespace SupermarketSystem.Application.Ordering.GetCustomerOrders;

public sealed record GetCustomerOrdersQuery(Guid CustomerId, PagedRequest Paging);

/// <summary>سجل طلبات زبون واحد - "إعادة طلب بضغطة" وتاريخ الطلبات بالتطبيق يعتمدوا عليها لاحقًا. يعيد استخدام OrderListItemDto نفسه (لا تكرار شكل DTO).</summary>
public sealed class GetCustomerOrdersHandler
{
    private readonly IApplicationDbContext _context;

    public GetCustomerOrdersHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<OrderListItemDto>> HandleAsync(GetCustomerOrdersQuery query, CancellationToken cancellationToken)
    {
        var paging = query.Paging.Normalized();

        var orders = _context.Orders.AsNoTracking()
            .Where(o => o.CustomerId == query.CustomerId)
            .OrderByDescending(o => o.CreatedAtUtc).ThenByDescending(o => o.Id);

        var totalCount = await orders.CountAsync(cancellationToken);

        var items = await orders
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Join(_context.Customers.AsNoTracking(), o => o.CustomerId, c => c.Id,
                (o, c) => new { Order = o, CustomerName = c.FullName, CustomerPhone = c.Phone })
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
