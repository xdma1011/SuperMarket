using SupermarketSystem.API.Common;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Application.Inventory.CreateStockTransfer;
using SupermarketSystem.Application.Inventory.GetStockTransferDetail;
using SupermarketSystem.Application.Inventory.GetStockTransfers;
using SupermarketSystem.Application.Inventory.GetProductBatchesWithStock;
using SupermarketSystem.Application.Inventory.ReceiveStockTransfer;

namespace SupermarketSystem.API.Endpoints;

/// <summary>كانت مفقودة بالكامل - راجع تعليق StockTransfer.cs بالـDomain.</summary>
public static class StockTransferEndpoints
{
    public static IEndpointRouteBuilder MapStockTransferEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/stock-transfers").WithTags("Inventory").RequirePermission(PermissionCodes.StockTransferManage);

        group.MapPost("/", async (
            CreateStockTransferCommand command,
            CreateStockTransferHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(command, cancellationToken);
            return result.ToHttpResult(response =>
                Results.Created($"/api/v1/stock-transfers/{response.StockTransferId}", response));
        })
        .WithName("CreateStockTransfer")
        .WithSummary("يرسل بضاعة من فرع لفرع - خطوة الإرسال فقط، تخصم من الفرع المصدر فورًا. الاستلام خطوة منفصلة لاحقة.")
        .Produces<CreateStockTransferResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/", async (
            int? pageNumber, int? pageSize, string? sortBy, string? sortDirection, Guid? branchId,
            GetStockTransfersHandler handler,
            CancellationToken cancellationToken) =>
        {
            var paging = PagingBinder.Build(pageNumber, pageSize, search: null, sortBy, sortDirection);
            var result = await handler.HandleAsync(new GetStockTransfersQuery(paging, branchId), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetStockTransfers")
        .Produces<PagedResult<StockTransferListItemDto>>(StatusCodes.Status200OK);

        group.MapGet("/product-batches", async (
            Guid productId, Guid branchId,
            GetProductBatchesWithStockHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new GetProductBatchesWithStockQuery(productId, branchId), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetProductBatchesWithStockForTransfer")
        .WithSummary("دفعات منتج فيها رصيد فعلي > 0 بفرع معيّن - لاختيار الدفعة المصدر وقت إرسال نقل مخزون.")
        .Produces<IReadOnlyList<ProductBatchWithStockDto>>(StatusCodes.Status200OK);

        group.MapGet("/{stockTransferId:guid}", async (
            Guid stockTransferId,
            GetStockTransferDetailHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new GetStockTransferDetailQuery(stockTransferId), cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("GetStockTransferDetail")
        .Produces<StockTransferDetailDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{stockTransferId:guid}/receive", async (
            Guid stockTransferId,
            ReceiveStockTransferHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new ReceiveStockTransferCommand(stockTransferId), cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("ReceiveStockTransfer")
        .WithSummary("يؤكّد وصول البضاعة فعليًا للفرع الوجهة - يضيفها لمخزونه الآن، ينشئ دفعة جديدة هناك لو أول مرة يوصلها رقم هذه الدفعة.")
        .Produces<ReceiveStockTransferResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }
}
