using SupermarketSystem.API.Common;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Ordering.CompleteDelivery;
using SupermarketSystem.Application.Ordering.GetMyDeliveries;

namespace SupermarketSystem.API.Endpoints;

/// <summary>
/// صفحة السائق - محمية بصلاحية Orders.Deliver وحدها (راجع تعليق
/// PermissionCodes.OrdersDeliver). كل الهوية (مين السائق) تُحسم من
/// التوكن (ICurrentUserContext داخل الـhandlers)، لا من أي معامل بالطلب.
/// </summary>
public static class DriverEndpoints
{
    public static IEndpointRouteBuilder MapDriverEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/driver").WithTags("Driver").RequirePermission(PermissionCodes.OrdersDeliver);

        group.MapGet("/my-deliveries", async (
            GetMyDeliveriesHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetMyDeliveries")
        .WithSummary("الطلبات المسندة للسائق الحالي، بانتظار التوصيل.")
        .Produces<IReadOnlyList<MyDeliveryDto>>(StatusCodes.Status200OK);

        group.MapPost("/deliveries/{orderId:guid}/complete", async (
            Guid orderId,
            CompleteDeliveryRequest request,
            CompleteDeliveryHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(
                new CompleteDeliveryCommand(orderId, request.PaymentMethodId, request.AmountCollected, request.ClientRequestId),
                cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("CompleteDelivery")
        .WithSummary("يؤكد تسليم الطلب واستلام الدفعة - ينشئ الفاتورة الحقيقية ويسجّل الكاش (نفس CompleteOrder، راجع CompleteDeliveryHandler).")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    public sealed record CompleteDeliveryRequest(Guid PaymentMethodId, decimal AmountCollected, Guid ClientRequestId);
}
