using SupermarketSystem.API.Common;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Application.CashManagement.CompleteCashClosing;
using SupermarketSystem.Application.CashManagement.GetCashClosings;
using SupermarketSystem.Application.CashManagement.RecordDrawerOpen;
using SupermarketSystem.Application.CashManagement.VarianceNotes;

namespace SupermarketSystem.API.Endpoints;

public static class CashManagementEndpoints
{
    public static IEndpointRouteBuilder MapCashManagementEndpoints(this IEndpointRouteBuilder app)
    {
        // برّا مجموعة cash-closings عمدًا (§3.4): صلاحيتها Sales.Create (أي كاشير)، مش CashClosing.Manage.
        app.MapPost("/api/v1/cash-drawer/open-events", async (
            RecordDrawerOpenCommand command,
            RecordDrawerOpenHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(command, cancellationToken);
            // إعادة إرسال فتحة محفوظة أوفلاين بنفس المفتاح = 200 بنفس السجل (مش 201 ولا تكرار).
            return result.ToHttpResult(response => response.WasReplay
                ? Results.Ok(response)
                : Results.Created($"/api/v1/cash-drawer/open-events/{response.DrawerOpenEventId}", response));
        })
        .WithTags("CashManagement")
        .RequirePermission(PermissionCodes.SalesCreate)
        .WithName("RecordDrawerOpen")
        .WithSummary("تسجيل فتح درج الكاش بلا بيع (زر فتح الصندوق بالكاشير) - تسجيل بس، بلا أثر على المبالغ.")
        .Produces<RecordDrawerOpenResponse>(StatusCodes.Status201Created)
        .Produces<RecordDrawerOpenResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        // تفسير فرق التقفيل (بند 23) - برّا المجموعة (§3.4): صاحب المحل/مساعد الأدمن بس (Returns.Review)، الكاشير ما بيفسّر فرق نفسه.
        app.MapGet("/api/v1/cash-closings/{id:guid}/variance-notes", async (
            Guid id, GetVarianceNotesHandler handler, CancellationToken cancellationToken) =>
            (await handler.HandleAsync(id, cancellationToken)).ToHttpResult())
        .WithTags("CashManagement")
        .RequirePermission(PermissionCodes.ReturnsReview)
        .WithName("GetCashClosingVarianceNotes")
        .WithSummary("تفسيرات فرق تقفيل صندوق + المفسَّر وغير المفسَّر.")
        .Produces<VarianceNotesResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapPost("/api/v1/cash-closings/{id:guid}/variance-notes", async (
            Guid id, RecordVarianceNoteRequest request, RecordVarianceNoteHandler handler, CancellationToken cancellationToken) =>
            (await handler.HandleAsync(
                new RecordVarianceNoteCommand(id, request.Reason, request.ExplainedAmount, request.Note, request.RelatedExpenseId),
                cancellationToken)).ToHttpResult())
        .WithTags("CashManagement")
        .RequirePermission(PermissionCodes.ReturnsReview)
        .WithName("RecordCashClosingVarianceNote")
        .WithSummary("تسجيل تفسير لفرق تقفيل صندوق - سجل تاريخي بلا أي أثر على التقفيل أو حركات الصندوق.")
        .Produces<VarianceNotesResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

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

public sealed record RecordVarianceNoteRequest(
    SupermarketSystem.Domain.CashManagement.VarianceExplanationReason Reason, decimal ExplainedAmount, string? Note, Guid? RelatedExpenseId);
