using SupermarketSystem.API.Common;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Application.Purchasing.CreateSupplier;
using SupermarketSystem.Application.Purchasing.GetSuppliers;
using SupermarketSystem.Application.Purchasing.UpdateSupplier;

namespace SupermarketSystem.API.Endpoints;

public static class SupplierEndpoints
{
    public static IEndpointRouteBuilder MapSupplierEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/suppliers").WithTags("Purchasing").RequirePermission(PermissionCodes.SuppliersManage);

        group.MapPost("/", async (
            CreateSupplierCommand command,
            CreateSupplierHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(command, cancellationToken);
            return result.ToHttpResult(response => Results.Created($"/api/v1/suppliers/{response.SupplierId}", response));
        })
        .WithName("CreateSupplier")
        .Produces<CreateSupplierResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/", async (
            int? pageNumber, int? pageSize, string? search, string? sortBy, string? sortDirection,
            GetSuppliersHandler handler,
            CancellationToken cancellationToken) =>
        {
            var paging = PagingBinder.Build(pageNumber, pageSize, search, sortBy, sortDirection);
            var result = await handler.HandleAsync(new GetSuppliersQuery(paging), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetSuppliers")
        .Produces<PagedResult<SupplierListItemDto>>(StatusCodes.Status200OK);

        group.MapPut("/{supplierId:guid}", async (
            Guid supplierId,
            UpdateSupplierRequest request,
            UpdateSupplierHandler handler,
            CancellationToken cancellationToken) =>
        {
            var command = new UpdateSupplierCommand(
                supplierId, request.Name, request.ContactName, request.Phone, request.Email);
            var result = await handler.HandleAsync(command, cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("UpdateSupplier")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    public sealed record UpdateSupplierRequest(string Name, string? ContactName, string? Phone, string? Email);
}
