using SupermarketSystem.API.Common;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Application.Ordering.AcceptOrder;
using SupermarketSystem.Application.Ordering.CompleteOrder;
using SupermarketSystem.Application.Ordering.GetCustomerOrders;
using SupermarketSystem.Application.Ordering.GetOrderById;
using SupermarketSystem.Application.Ordering.GetPendingOrders;
using SupermarketSystem.Application.Ordering.PlaceOrder;
using SupermarketSystem.Application.Ordering.RejectOrder;
using SupermarketSystem.Domain.Ordering;

namespace SupermarketSystem.API.Endpoints;

/// <summary>
/// أساس تطبيق الزبائن (نقاش صاحب المشروع - راجع دورة حياة Order.cs).
///
/// ⚠️ تحذير أمني صريح لازم يُحل قبل أي إطلاق فعلي: PlaceOrder وGetCustomerOrders
/// حاليًا AllowAnonymous بلا أي تحقق هوية حقيقي - رقم الهاتف يُرسَل كنص
/// عادي بلا إثبات ملكية. هذا مؤقت بالكامل لحد ما تُبنى مصادقة الزبائن
/// الحقيقية (تحقق OTP عبر تلغرام - نقاش منفصل لسا ما انبنى). ممنوع
/// اعتماد هالحالة بإنتاج فعلي.
///
/// شاشات الكاشير (قبول/رفض/إكمال) محمية بصلاحية Sales.Create الموجودة
/// أصلًا - القبول والإكمال بالنهاية عملية بيع حقيقية.
/// </summary>
public static class OrderingEndpoints
{
    public static IEndpointRouteBuilder MapOrderingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/orders", async (
            PlaceOrderCommand command,
            PlaceOrderHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(command, cancellationToken);
            return result.ToHttpResult(response => Results.Created($"/api/v1/orders/{response.OrderId}", response));
        })
        .WithName("PlaceOrder")
        .WithTags("Ordering")
        .AllowAnonymous()
        .WithSummary("⚠️ مؤقت بلا تحقق هوية حقيقي - يقدّم طلب زبون جديد (Pending)، بلا أي تأثير على المخزون لحد ما يُقبل ويُكمَّل.")
        .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        app.MapGet("/api/v1/orders/customers/{customerId:guid}", async (
            Guid customerId, int? pageNumber, int? pageSize,
            GetCustomerOrdersHandler handler,
            CancellationToken cancellationToken) =>
        {
            var paging = PagingBinder.Build(pageNumber, pageSize, search: null, sortBy: null, sortDirection: null);
            var result = await handler.HandleAsync(new GetCustomerOrdersQuery(customerId, paging), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetCustomerOrders")
        .WithTags("Ordering")
        .AllowAnonymous()
        .WithSummary("⚠️ مؤقت بلا تحقق هوية حقيقي - سجل طلبات زبون واحد.")
        .Produces<PagedResult<OrderListItemDto>>(StatusCodes.Status200OK);

        var cashierGroup = app.MapGroup("/api/v1/orders").WithTags("Ordering").RequirePermission(PermissionCodes.SalesCreate);

        cashierGroup.MapGet("/", async (
            int? pageNumber, int? pageSize, Guid? branchId, int? status,
            GetPendingOrdersHandler handler,
            CancellationToken cancellationToken) =>
        {
            var paging = PagingBinder.Build(pageNumber, pageSize, search: null, sortBy: null, sortDirection: null);
            var statusFilter = status is { } s ? (OrderStatus)s : (OrderStatus?)null;
            var result = await handler.HandleAsync(new GetPendingOrdersQuery(paging, branchId, statusFilter), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetPendingOrders")
        .WithSummary("قائمة الطلبات (بانتظار القبول أو مقبولة قيد التجهيز افتراضيًا).")
        .Produces<PagedResult<OrderListItemDto>>(StatusCodes.Status200OK);

        cashierGroup.MapGet("/{orderId:guid}", async (
            Guid orderId,
            GetOrderByIdHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new GetOrderByIdQuery(orderId), cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("GetOrderById")
        .WithSummary("تفاصيل طلب واحد كاملة، بكل أسطره.")
        .Produces<OrderDetailDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        cashierGroup.MapPost("/{orderId:guid}/accept", async (
            Guid orderId,
            AcceptOrderHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new AcceptOrderCommand(orderId), cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("AcceptOrder")
        .WithSummary("يقبل الطلب (بانتظار التجهيز) - بلا خصم مخزون أو فاتورة بعد.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        cashierGroup.MapPost("/{orderId:guid}/reject", async (
            Guid orderId,
            RejectOrderRequest request,
            RejectOrderHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new RejectOrderCommand(orderId, request.Reason), cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("RejectOrder")
        .WithSummary("يرفض الطلب - سبب إلزامي دائمًا.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        cashierGroup.MapPost("/{orderId:guid}/complete", async (
            Guid orderId,
            CompleteOrderRequest request,
            CompleteOrderHandler handler,
            CancellationToken cancellationToken) =>
        {
            var command = new CompleteOrderCommand(orderId, request.Payments, request.ClientRequestId);
            var result = await handler.HandleAsync(command, cancellationToken);
            return result.ToHttpResult(response => Results.Created($"/api/v1/sales/{response.SaleInvoiceId}", response));
        })
        .WithName("CompleteOrder")
        .WithSummary("لحظة التسليم الفعلية - يحوّل الطلب المقبول لفاتورة بيع حقيقية (تخصم المخزون فعليًا).")
        .Produces(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    public sealed record RejectOrderRequest(string Reason);

    public sealed record CompleteOrderRequest(
        IReadOnlyList<Application.Ordering.CompleteOrder.CompleteOrderPaymentDto> Payments, Guid ClientRequestId);
}
