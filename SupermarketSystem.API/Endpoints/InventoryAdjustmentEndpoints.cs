using SupermarketSystem.API.Common;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Application.Inventory.GetCurrentStock;
using SupermarketSystem.Application.Inventory.RecordComplimentaryIssue;

namespace SupermarketSystem.API.Endpoints;

public static class InventoryAdjustmentEndpoints
{
    public static IEndpointRouteBuilder MapInventoryAdjustmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/inventory")
            .WithTags("Inventory")
            .RequirePermission(PermissionCodes.ComplimentaryIssue);

        group.MapPost("/complimentary-issues", async (
            RecordComplimentaryIssueCommand command,
            RecordComplimentaryIssueHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(command, cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("RecordComplimentaryIssue")
        .WithSummary("يسجّل خروج بضاعة كضيافة/استهلاك داخلي - ينقص المخزون بلا أي قيد مالي كإيراد.")
        .Produces<RecordComplimentaryIssueResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // خارج المجموعة عمدًا (لا group.MapGet) — المجموعة مقفولة بصلاحية
        // الضيافة الضيقة، بينما عرض المخزون استخدام عام (كتالوج، تقارير)
        // لازم صلاحية أوسع. نفس الفخ اللي انتبهنا له قبل مع GetSupplierDebts.
        app.MapGet("/api/v1/inventory/current-stock", async (
            int? pageNumber, int? pageSize, string? search, string? sortBy, string? sortDirection,
            Guid? branchId,
            GetCurrentStockHandler handler,
            CancellationToken cancellationToken) =>
        {
            var paging = PagingBinder.Build(pageNumber, pageSize, search, sortBy, sortDirection);
            var result = await handler.HandleAsync(new GetCurrentStockQuery(paging, branchId), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetCurrentStock")
        .WithTags("Inventory")
        .RequirePermission(PermissionCodes.ReportsView)
        .WithSummary("المخزون الحالي مجمَّعًا حسب (منتج، فرع) - كم عندي من كل صنف الآن.")
        .Produces<PagedResult<CurrentStockItemDto>>(StatusCodes.Status200OK);

        return app;
    }
}
