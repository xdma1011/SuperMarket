using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;

namespace SupermarketSystem.Application.Ordering.GetOrderById;

/// <summary>
/// IgnoreBranchFilter=true حصرًا لمسار الزبون المجهول (GetOrderByIdForCustomer -
/// AllowAnonymous، بلا claim فرع أصلًا فمرشِّح الفرع العام بيحجب كل شي).
/// المسار المحمي (شاشة الكاشير، cashierGroup) لازم يضل يستخدم false
/// الافتراضية - إزالة الفلتر لكل الاستدعاءات كانت رح تسمح لكاشير بفرع
/// معيّن يشوف تفاصيل طلب فرع تاني بالكامل، خرق مباشر لعزل الفروع.
/// </summary>
public sealed record GetOrderByIdQuery(Guid OrderId, bool IgnoreBranchFilter = false);

public sealed record OrderItemDetailDto(
    Guid ProductId, string ProductName, Guid ProductUnitId, string UnitName, decimal Quantity, decimal EstimatedUnitPrice);

public sealed record OrderDetailDto(
    Guid Id,
    Guid CustomerId,
    string CustomerName,
    string? CustomerPhone,
    Guid BranchId,
    int Status,
    string? DeliveryNote,
    decimal? DeliveryLatitude,
    decimal? DeliveryLongitude,
    string? RejectionReason,
    Guid? ResultingSaleInvoiceId,
    IReadOnlyList<OrderItemDetailDto> Items,
    DateTime CreatedAtUtc,
    DateTime? DecidedAtUtc,
    int? Rating,
    string? RatingComment);

public sealed class GetOrderByIdHandler
{
    private readonly IApplicationDbContext _context;

    public GetOrderByIdHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<OrderDetailDto>> HandleAsync(GetOrderByIdQuery query, CancellationToken cancellationToken)
    {
        // Include(Items) إلزامي هون: order.Items تُقرأ لاحقًا بكود C# عادي
        // (plain LINQ-to-Objects) بعد التجسيد، لا داخل استعلام EF مترجَم -
        // بدونها المجموعة تضل فاضية دائمًا (خلاف GetPendingOrders/GetCustomerOrders
        // اللي بيستخدموا order.Items.Sum/Count داخل IQueryable نفسه، فبيترجم
        // لـSQL صحيح بلا حاجة Include).
        var ordersQuery = _context.Orders.AsNoTracking();
        if (query.IgnoreBranchFilter)
        {
            ordersQuery = ordersQuery.IgnoreQueryFilters();
        }

        var order = await ordersQuery
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == query.OrderId, cancellationToken);

        if (order is null)
        {
            return Result.Failure<OrderDetailDto>(Error.NotFound("Order.NotFound", $"الطلب '{query.OrderId}' غير موجود."));
        }

        var customer = await _context.Customers.AsNoTracking()
            .Where(c => c.Id == order.CustomerId)
            .Select(c => new { c.FullName, c.Phone })
            .FirstAsync(cancellationToken);

        var productIds = order.Items.Select(i => i.ProductId).Distinct().ToList();
        var productNames = await _context.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);

        var unitIds = order.Items.Select(i => i.ProductUnitId).Distinct().ToList();
        var unitNames = await _context.ProductUnits.AsNoTracking()
            .Where(u => unitIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.UnitName, cancellationToken);

        var items = order.Items
            .Select(i => new OrderItemDetailDto(
                i.ProductId,
                productNames.GetValueOrDefault(i.ProductId, "—"),
                i.ProductUnitId,
                unitNames.GetValueOrDefault(i.ProductUnitId, "—"),
                i.Quantity,
                i.EstimatedUnitPrice))
            .ToList();

        return Result.Success(new OrderDetailDto(
            order.Id,
            order.CustomerId,
            customer.FullName,
            customer.Phone,
            order.BranchId,
            (int)order.Status,
            order.DeliveryNote,
            order.DeliveryLatitude,
            order.DeliveryLongitude,
            order.RejectionReason,
            order.ResultingSaleInvoiceId,
            items,
            order.CreatedAtUtc,
            order.DecidedAtUtc,
            order.Rating,
            order.RatingComment));
    }
}
