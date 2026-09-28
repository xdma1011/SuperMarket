using SupermarketSystem.API.Common;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Application.Reporting.GetRecentReturns;
using SupermarketSystem.Application.Sales.MarkReturnReviewed;
using SupermarketSystem.Application.Sales.ProcessReturn;

namespace SupermarketSystem.API.Endpoints;

public static class ReturnEndpoints
{
    public static IEndpointRouteBuilder MapReturnEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/returns").WithTags("Sales");

        // قائمة فواتير الإرجاع (صفحة الإرجاعات) - نفس استعلام تقرير "الإرجاعات الأخيرة"، بس بصلاحية
        // معالجة الإرجاع بدل عرض التقارير، عشان صفحة الإرجاعات ما تحتاج صلاحية التقارير.
        group.MapGet("/", async (
            int? pageNumber, int? pageSize, string? sortBy, string? sortDirection,
            Guid? branchId, DateTime? fromUtc, DateTime? toUtc,
            GetRecentReturnsHandler handler,
            CancellationToken cancellationToken) =>
        {
            var paging = PagingBinder.Build(pageNumber, pageSize, search: null, sortBy, sortDirection);
            return Results.Ok(await handler.HandleAsync(new GetRecentReturnsQuery(paging, branchId, fromUtc, toUtc), cancellationToken));
        })
        .WithName("GetReturnInvoices")
        .RequirePermission(PermissionCodes.ReturnsProcess)
        .WithSummary("فواتير الإرجاع - الأحدث أولًا، مع رقم فاتورة البيع الأصلية.")
        .Produces<PagedResult<RecentReturnItemDto>>(StatusCodes.Status200OK);

        group.MapPost("/", async (
            ProcessReturnCommand command,
            ProcessReturnHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(command, cancellationToken);

            return result.ToHttpResult(response =>
                // إعادة الطلب بنفس المفتاح بترجّع 200 لا 201 — الإرجاع
                // أُنشئ بالطلب الأصلي، لا بهذا. الكاشير بيعرف من رمز
                // الحالة إنه ما كرّر الإرجاع.
                response.WasReplay
                    ? Results.Ok(response)
                    : Results.Created($"/api/v1/returns/{response.ReturnInvoiceId}", response));
        })
        .WithName("ProcessReturn")
        .RequirePermission(PermissionCodes.ReturnsProcess)
        .WithSummary("يعالج إرجاع من زبون: يحرس الكمية ذريًا، يرجّع البضاعة للمخزون (لنفس دفعتها الأصلية)، يسجّل الاسترجاع وحركة الدرج، ويحدّث حالة الفاتورة الأصلية — كله بمعاملة واحدة.")
        .Produces<ProcessReturnResponse>(StatusCodes.Status201Created)
        .Produces<ProcessReturnResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{returnInvoiceId:guid}/mark-reviewed", async (
            Guid returnInvoiceId,
            MarkReturnReviewedHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new MarkReturnReviewedCommand(returnInvoiceId), cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("MarkReturnReviewed")
        .RequirePermission(PermissionCodes.ReturnsReview)
        .WithSummary("يضع علامة 'تمت المراجعة' على إرجاع — إجراء إداري لاحق بلا أي أثر مالي. الإرجاع يبقى ظاهرًا كإرجاع دائمًا.")
        .Produces<MarkReturnReviewedResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }
}
