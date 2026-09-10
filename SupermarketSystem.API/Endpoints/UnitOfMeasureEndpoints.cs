using SupermarketSystem.API.Common;
using SupermarketSystem.Application.Catalog.CreateUnitOfMeasure;
using SupermarketSystem.Application.Catalog.GetUnitsOfMeasure;
using SupermarketSystem.Application.Catalog.SetUnitOfMeasureActive;
using SupermarketSystem.Application.Common.Interfaces;

namespace SupermarketSystem.API.Endpoints;

/// <summary>كانت مفقودة بالكامل - راجع تعليق UnitOfMeasure.cs بالـDomain.</summary>
public static class UnitOfMeasureEndpoints
{
    public static IEndpointRouteBuilder MapUnitOfMeasureEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/units-of-measure").WithTags("Catalog").RequirePermission(PermissionCodes.CatalogManage);

        group.MapGet("/", async (
            bool? activeOnly,
            GetUnitsOfMeasureHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new GetUnitsOfMeasureQuery(activeOnly ?? false), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetUnitsOfMeasure")
        .WithSummary("قائمة وحدات القياس المرجعية - قائمة صغيرة بلا ترقيم صفحات.")
        .Produces<IReadOnlyList<UnitOfMeasureDto>>(StatusCodes.Status200OK);

        group.MapPost("/", async (
            CreateUnitOfMeasureCommand command,
            CreateUnitOfMeasureHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(command, cancellationToken);
            return result.ToHttpResult(response =>
                Results.Created($"/api/v1/units-of-measure/{response.UnitOfMeasureId}", response));
        })
        .WithName("CreateUnitOfMeasure")
        .Produces<CreateUnitOfMeasureResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{unitOfMeasureId:guid}/active", async (
            Guid unitOfMeasureId,
            SetUnitOfMeasureActiveRequest request,
            SetUnitOfMeasureActiveHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(
                new SetUnitOfMeasureActiveCommand(unitOfMeasureId, request.IsActive), cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("SetUnitOfMeasureActive")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    public sealed record SetUnitOfMeasureActiveRequest(bool IsActive);
}
