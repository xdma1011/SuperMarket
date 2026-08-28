using SupermarketSystem.API.Common;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Application.Inventory.ApproveStocktake;
using SupermarketSystem.Application.Inventory.CompleteStocktake;
using SupermarketSystem.Application.Inventory.CreateStocktake;
using SupermarketSystem.Application.Inventory.GetStocktakeById;
using SupermarketSystem.Application.Inventory.GetStocktakes;
using SupermarketSystem.Application.Inventory.RecordStocktakeCount;

namespace SupermarketSystem.API.Endpoints;

public static class StocktakeEndpoints
{
    public static IEndpointRouteBuilder MapStocktakeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/stocktakes").WithTags("Inventory");

        group.MapPost("/", async (
            CreateStocktakeCommand command,
            CreateStocktakeHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(command, cancellationToken);
            return result.ToHttpResult(response => Results.Created($"/api/v1/stocktakes/{response.StocktakeId}", response));
        })
        .WithName("CreateStocktake")
        .RequirePermission(PermissionCodes.StocktakeManage)
        .WithSummary("ينشئ جرد جديد بنطاق مرن: أصناف مختارة أو الفرع كامل. يبدأ InProgress مباشرة، جاهز للعدّ فورًا.")
        .Produces<CreateStocktakeResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/", async (
            int? pageNumber, int? pageSize, string? search, string? sortBy, string? sortDirection,
            Guid? branchId,
            GetStocktakesHandler handler,
            CancellationToken cancellationToken) =>
        {
            var paging = PagingBinder.Build(pageNumber, pageSize, search, sortBy, sortDirection);
            var result = await handler.HandleAsync(new GetStocktakesQuery(paging, branchId), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetStocktakes")
        .RequirePermission(PermissionCodes.StocktakeManage)
        .WithSummary("كل عمليات الجرد، الأحدث أولًا - كانت ناقصة، بلا طريقة تشوف كل الجرد إلا بمعرّف واحد بمعرفة.")
        .Produces<PagedResult<StocktakeListItemDto>>(StatusCodes.Status200OK);

        group.MapGet("/{stocktakeId:guid}", async (
            Guid stocktakeId,
            GetStocktakeByIdHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new GetStocktakeByIdQuery(stocktakeId), cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("GetStocktakeById")
        .RequirePermission(PermissionCodes.StocktakeManage)
        .Produces<StocktakeDetailResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{stocktakeId:guid}/items/{stocktakeItemId:guid}/count", async (
            Guid stocktakeId,
            Guid stocktakeItemId,
            RecordStocktakeCountRequest request,
            RecordStocktakeCountHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(
                new RecordStocktakeCountCommand(stocktakeId, stocktakeItemId, request.CountedQuantity), cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("RecordStocktakeCount")
        .RequirePermission(PermissionCodes.StocktakeManage)
        .WithSummary("يسجّل عدّ فعلي لصنف واحد. أي مستخدم مصرَّح له يقدر يستدعيها لأي سطر، بشكل مستقل عن باقي المستخدمين.")
        .Produces<RecordStocktakeCountResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{stocktakeId:guid}/complete", async (
            Guid stocktakeId,
            CompleteStocktakeHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new CompleteStocktakeCommand(stocktakeId), cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("CompleteStocktake")
        .RequirePermission(PermissionCodes.StocktakeManage)
        .WithSummary("يقفل مرحلة العدّ ويرجّع كل الفروقات للمراجعة. لا يلمس المخزون إطلاقًا.")
        .Produces<CompleteStocktakeResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{stocktakeId:guid}/approve", async (
            Guid stocktakeId,
            ApproveStocktakeHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new ApproveStocktakeCommand(stocktakeId), cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("ApproveStocktake")
        .RequirePermission(PermissionCodes.StocktakeApprove)
        .WithSummary("يعتمد الجرد نهائيًا: يطبّق كل التصحيحات على المخزون فعليًا (StockMovement + Stock)، بمعاملة واحدة.")
        .Produces<ApproveStocktakeResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    public sealed record RecordStocktakeCountRequest(decimal CountedQuantity);
}
