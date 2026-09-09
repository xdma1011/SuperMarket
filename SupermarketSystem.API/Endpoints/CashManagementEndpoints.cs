using SupermarketSystem.API.Common;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Application.CashManagement.CompleteCashClosing;
using SupermarketSystem.Application.CashManagement.GetCashClosings;

namespace SupermarketSystem.API.Endpoints;

public static class CashManagementEndpoints
{
    public static IEndpointRouteBuilder MapCashManagementEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/cash-closings").WithTags("CashManagement").RequirePermission(PermissionCodes.CashClosingManage);

        group.MapPost("/", async (
            CompleteCashClosingCommand command,
            CompleteCashClosingHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(command, cancellationToken);
            return result.ToHttpResult(response =>
                Results.Created($"/api/v1/cash-closings/{response.CashClosingId}", response));
        })
        .WithName("CompleteCashClosing")
        .WithSummary("يقفّل صندوق الفرع لليوم التجاري المحدد: يحسب المتوقع من CashDrawerLog وسجلات الدفع، ويقارنه بالمعدود فعليًا.")
        .Produces<CompleteCashClosingResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/", async (
            int? pageNumber, int? pageSize, string? sortBy, string? sortDirection, Guid? branchId,
            GetCashClosingsHandler handler,
            CancellationToken cancellationToken) =>
        {
            var paging = PagingBinder.Build(pageNumber, pageSize, search: null, sortBy, sortDirection);
            var result = await handler.HandleAsync(new GetCashClosingsQuery(paging, branchId), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetCashClosings")
        .WithSummary("قائمة تقفيلات الصندوق السابقة، مع فرز حسب اليوم التجاري وفلترة اختيارية بالفرع.")
        .Produces<PagedResult<CashClosingListItemDto>>(StatusCodes.Status200OK);

        return app;
    }
}
