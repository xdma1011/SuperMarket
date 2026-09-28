using SupermarketSystem.API.Common;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Sales.AtCostWithdrawal;
using SupermarketSystem.Application.Sales.PreparedOrders;

namespace SupermarketSystem.API.Endpoints;

/// <summary>
/// صفحة التلفون (28/9/2026): ضرب باركود، طلب جاهز لمساعد الكاشير، وسحب بضاعة بسعر التكلفة.
/// السحب بصلاحيته الخاصة (Sales.AtCostWithdrawal) برّا أي مجموعة - راجع §3.4 (الصلاحيات بتتراكم).
/// </summary>
public static class SaleAssistEndpoints
{
    public static IEndpointRouteBuilder MapSaleAssistEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/sales/scan-lookup", async (
            Guid branchId,
            string term,
            ScanLookupHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new ScanLookupQuery(branchId, term), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("ScanLookup")
        .WithTags("Sales")
        .RequirePermission(PermissionCodes.SalesCreate)
        .WithSummary("باركود (مطابق حرفيًا) أو اسم منتج -> المنتج بوحدته وسعر الفرع.")
        .Produces<IReadOnlyList<ScanLookupItemDto>>(StatusCodes.Status200OK);

        var prepared = app.MapGroup("/api/v1/prepared-orders")
            .WithTags("PreparedOrders")
            .RequirePermission(PermissionCodes.SalesCreate);

        prepared.MapPost("/", async (
            CreatePreparedOrderCommand command,
            CreatePreparedOrderHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(command, cancellationToken);
            return result.ToHttpResult(response => Results.Created($"/api/v1/prepared-orders/{response.PreparedOrderId}", response));
        })
        .WithName("CreatePreparedOrder")
        .WithSummary("مساعد الكاشير: طلب جاهز برقم قصير، الكاشير بيحاسب عليه - بلا أي أثر مالي أو مخزني لحد البيع.")
        .Produces<CreatePreparedOrderResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        prepared.MapGet("/", async (
            Guid? branchId,
            GetOpenPreparedOrdersHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new GetOpenPreparedOrdersQuery(branchId), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetOpenPreparedOrders")
        .WithSummary("الطلبات الجاهزة المفتوحة (آخر 24 ساعة)، الأقدم أول.")
        .Produces<IReadOnlyList<PreparedOrderDto>>(StatusCodes.Status200OK);

        prepared.MapPost("/{preparedOrderId:guid}/cancel", async (
            Guid preparedOrderId,
            CancelPreparedOrderHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new CancelPreparedOrderCommand(preparedOrderId), cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("CancelPreparedOrder")
        .WithSummary("إلغاء طلب جاهز ما انحاسب.")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        app.MapPost("/api/v1/sales/at-cost/quote", async (
            QuoteAtCostWithdrawalQuery query,
            QuoteAtCostWithdrawalHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(query, cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("QuoteAtCostWithdrawal")
        .WithTags("Sales")
        .RequirePermission(PermissionCodes.SalesAtCostWithdrawal)
        .WithSummary("تكلفة الأصناف قبل السحب بسعر التكلفة - بلا تسجيل.")
        .Produces<AtCostQuoteDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        app.MapPost("/api/v1/sales/at-cost", async (
            CompleteAtCostWithdrawalCommand command,
            CompleteAtCostWithdrawalHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(command, cancellationToken);
            return result.ToHttpResult(response =>
                response.WasReplay
                    ? Results.Ok(response)
                    : Results.Created($"/api/v1/sales/{response.SaleInvoiceId}", response));
        })
        .WithName("CompleteAtCostWithdrawal")
        .WithTags("Sales")
        .RequirePermission(PermissionCodes.SalesAtCostWithdrawal)
        .WithSummary("سحب بضاعة لصاحب المحل/شريك بسعر التكلفة: دفع حقها (للصندوق) أو اخصمها مني (مسجّلة عليه).")
        .Produces<CompleteAtCostWithdrawalResponse>(StatusCodes.Status201Created)
        .Produces<CompleteAtCostWithdrawalResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }
}
