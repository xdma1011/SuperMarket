using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Domain.Ordering;

namespace SupermarketSystem.Application.Ordering.GetMyDeliveries;

public sealed record MyDeliveryDto(
    Guid OrderId,
    string CustomerName,
    string? CustomerPhone,
    string? DeliveryNote,
    decimal? DeliveryLatitude,
    decimal? DeliveryLongitude,
    decimal EstimatedTotal,
    int ItemCount,
    DateTime CreatedAtUtc);

/// <summary>
/// صفحة السائق الوحيدة - طلباته هو فقط، بحالة Accepted (لسا ما وصلت،
/// مو تاريخ قديم). DriverId يجي من ICurrentUserContext.UserId هون داخل
/// الـhandler نفسه - لا من الطلب، وإلا سائق يقدر يشوف طلبات سائق ثاني
/// بس يبدّل رقم بالـURL (نفس نمط AcceptOrderHandler بالضبط).
/// </summary>
public sealed class GetMyDeliveriesHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;

    public GetMyDeliveriesHandler(IApplicationDbContext context, ICurrentUserContext currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<MyDeliveryDto>> HandleAsync(CancellationToken cancellationToken)
    {
        var driverId = _currentUser.UserId
            ?? throw new InvalidOperationException("لا يمكن جلب طلبات التوصيل بلا هوية مستخدم مصادَق عليها.");

        return await _context.Orders.AsNoTracking()
            .Where(o => o.DriverId == driverId && o.Status == OrderStatus.Accepted)
            .OrderBy(o => o.DriverAssignedAtUtc)
            .Join(_context.Customers.AsNoTracking(), o => o.CustomerId, c => c.Id,
                (o, c) => new { Order = o, CustomerName = c.FullName, CustomerPhone = c.Phone })
            .Select(x => new MyDeliveryDto(
                x.Order.Id,
                x.CustomerName,
                x.CustomerPhone,
                x.Order.DeliveryNote,
                x.Order.DeliveryLatitude,
                x.Order.DeliveryLongitude,
                x.Order.Items.Sum(i => i.Quantity * i.EstimatedUnitPrice),
                x.Order.Items.Count,
                x.Order.CreatedAtUtc))
            .ToListAsync(cancellationToken);
    }
}
