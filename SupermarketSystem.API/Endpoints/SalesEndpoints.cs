using SupermarketSystem.API.Common;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Application.Sales.CompleteSale;
using SupermarketSystem.Application.Sales.GetSaleInvoiceById;
using SupermarketSystem.Application.Sales.GetSaleInvoices;
using SupermarketSystem.Application.Sales.VoidSale;
using SupermarketSystem.Domain.Sales;

namespace SupermarketSystem.API.Endpoints;

public static class SalesEndpoints
{
    public static IEndpointRouteBuilder MapSalesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/sales").WithTags("Sales");

        group.MapPost("/", async (
            CompleteSaleCommand command,
            CompleteSaleHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(command, cancellationToken);

            return result.ToHttpResult(response =>
                // A replay returns 200, not 201: the resource was created by
                // the ORIGINAL request, not this one. A client retrying after
                // a network timeout gets the same sale back and can tell from
                // the status code that it did not ring up a second one.
                response.WasReplay
                    ? Results.Ok(response)
                    : Results.Created($"/api/v1/sales/{response.SaleInvoiceId}", response));
        })
        .WithName("CompleteSale")
        .RequirePermission(PermissionCodes.SalesCreate)
        .WithSummary("Completes a sale: decrements stock atomically, records the invoice, payments, stock movements and cash-drawer entries in one transaction.")
        .Produces<CompleteSaleResponse>(StatusCodes.Status201Created)
        .Produces<CompleteSaleResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{saleInvoiceId:guid}/void", async (
            Guid saleInvoiceId,
            VoidSaleRequest request,
            VoidSaleHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(
                new VoidSaleCommand(saleInvoiceId, request.Reason, request.Notes), cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("VoidSale")
        .RequirePermission(PermissionCodes.SalesVoid)
        .WithSummary("يلغي فاتورة بيع مكتملة: الفاتورة تبقى محفوظة بحالة Voided، ويُعكس المخزون والدفعات وحركات الدرج بمعاملة واحدة.")
        .Produces<VoidSaleResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/", async (
            int? pageNumber, int? pageSize, string? search, string? sortBy, string? sortDirection,
            Guid? branchId,
            GetSaleInvoicesHandler handler,
            CancellationToken cancellationToken) =>
        {
            var paging = PagingBinder.Build(pageNumber, pageSize, search, sortBy, sortDirection);
            var result = await handler.HandleAsync(new GetSaleInvoicesQuery(paging, branchId), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetSaleInvoices")
        .RequirePermission(PermissionCodes.SalesCreate)
        .WithSummary("قائمة/بحث فواتير البيع - أساس البحث عن فاتورة أصلية قبل أي عملية إرجاع.")
        .Produces<PagedResult<SaleInvoiceListItemDto>>(StatusCodes.Status200OK);

        group.MapGet("/{saleInvoiceId:guid}", async (
            Guid saleInvoiceId,
            GetSaleInvoiceByIdHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new GetSaleInvoiceByIdQuery(saleInvoiceId), cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("GetSaleInvoiceById")
        .RequirePermission(PermissionCodes.SalesCreate)
        .WithSummary("تفاصيل فاتورة بيع بأصنافها الكاملة - كل سطر بكميته القابلة للإرجاع (Quantity - QuantityReturned).")
        .Produces<SaleInvoiceDetailDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    /// <summary>SaleInvoiceId يجي من المسار لا من الجسم.</summary>
    public sealed record VoidSaleRequest(VoidReason Reason, string? Notes);
}
