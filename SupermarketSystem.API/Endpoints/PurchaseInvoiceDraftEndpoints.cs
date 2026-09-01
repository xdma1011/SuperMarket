using SupermarketSystem.API.Common;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Application.Purchasing.PurchaseInvoiceDrafts;
using SupermarketSystem.Domain.Purchasing;

namespace SupermarketSystem.API.Endpoints;

/// <summary>
/// عمدًا مش داخل مجموعة "/api/v1/purchase-invoices" بـPurchasingEndpoints.cs
/// (اللي عندها RequirePermission(PurchasingCreate) على كامل المجموعة) -
/// لو نزّلنا endpoint إنشاء المسودة جوّاها، الفلاتر تتراكم AND (راجع
/// CLAUDE.md §3.4)، فيصير محتاج PurchasingCreate و PurchasingCreateDraft
/// معًا - يلغي بالضبط الهدف من فصل الصلاحيتين (الكاشير يرفع مسودة بلا
/// PurchasingCreate). كل endpoint هون آخذ صلاحيته صراحة لحاله.
/// </summary>
public static class PurchaseInvoiceDraftEndpoints
{
    public static IEndpointRouteBuilder MapPurchaseInvoiceDraftEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/purchase-invoices/drafts/from-image", async (
            IFormFile file,
            Guid branchId,
            decimal? paidNowAmount,
            Guid? paidNowPaymentMethodId,
            CreatePurchaseInvoiceDraftFromImageHandler handler,
            CancellationToken cancellationToken) =>
        {
            if (file.Length == 0)
            {
                return Results.Problem(title: "Image.Empty", detail: "لم يتم رفع أي ملف.", statusCode: StatusCodes.Status400BadRequest);
            }

            byte[] imageBytes;
            await using (var memoryStream = new MemoryStream())
            {
                await file.CopyToAsync(memoryStream, cancellationToken);
                imageBytes = memoryStream.ToArray();
            }

            var result = await handler.HandleAsync(
                new CreatePurchaseInvoiceDraftFromImageCommand(branchId, imageBytes, file.ContentType, paidNowAmount, paidNowPaymentMethodId),
                cancellationToken);

            return result.ToHttpResult(response =>
                Results.Created($"/api/v1/purchase-invoices/drafts/{response.DraftId}", response));
        })
        .WithName("CreatePurchaseInvoiceDraftFromImage")
        .WithTags("Purchasing")
        .RequirePermission(PermissionCodes.PurchasingCreateDraft)
        .WithSummary("يرفع صورة فاتورة شراء، يقرأها بالذكاء الاصطناعي، ويحفظها كمسودة (PendingReview) - بلا أي تأثير على المخزون لحد ما تُعتمد. صلاحية أضعف من Purchasing.Create عمدًا، متاحة للكاشير افتراضيًا.")
        .DisableAntiforgery()
        .Produces<CreatePurchaseInvoiceDraftFromImageResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        app.MapGet("/api/v1/purchase-invoices/drafts", async (
            int? pageNumber, int? pageSize, string? search, string? sortBy, string? sortDirection,
            Guid? branchId, int? status,
            GetPurchaseInvoiceDraftsHandler handler,
            CancellationToken cancellationToken) =>
        {
            var paging = PagingBinder.Build(pageNumber, pageSize, search, sortBy, sortDirection);
            var statusFilter = status is { } s ? (PurchaseInvoiceDraftStatus)s : (PurchaseInvoiceDraftStatus?)null;

            var result = await handler.HandleAsync(new GetPurchaseInvoiceDraftsQuery(paging, branchId, statusFilter), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetPurchaseInvoiceDrafts")
        .WithTags("Purchasing")
        .RequirePermission(PermissionCodes.PurchasingCreate)
        .WithSummary("قائمة مسودات فواتير الشراء (PendingReview افتراضيًا) - يحتاج Purchasing.Create، لا يكفي رفعها فقط.")
        .Produces<PagedResult<PurchaseInvoiceDraftListItemDto>>(StatusCodes.Status200OK);

        app.MapGet("/api/v1/purchase-invoices/drafts/{draftId:guid}", async (
            Guid draftId,
            GetPurchaseInvoiceDraftByIdHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new GetPurchaseInvoiceDraftByIdQuery(draftId), cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("GetPurchaseInvoiceDraftById")
        .WithTags("Purchasing")
        .RequirePermission(PermissionCodes.PurchasingCreate)
        .WithSummary("تفاصيل مسودة فاتورة واحدة، بكل أسطرها، للمراجعة/التعديل.")
        .Produces<PurchaseInvoiceDraftDetailDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet("/api/v1/purchase-invoices/drafts/{draftId:guid}/image", async (
            Guid draftId,
            GetPurchaseInvoiceDraftImagePathHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new GetPurchaseInvoiceDraftImagePathQuery(draftId), cancellationToken);
            if (result.IsFailure)
            {
                return result.ToHttpResult();
            }

            if (!File.Exists(result.Value))
            {
                return Results.Problem(title: "Image.FileMissing", detail: "الصورة المحفوظة غير موجودة على القرص.", statusCode: StatusCodes.Status404NotFound);
            }

            var bytes = await File.ReadAllBytesAsync(result.Value, cancellationToken);
            return Results.File(bytes, "image/webp");
        })
        .WithName("GetPurchaseInvoiceDraftImage")
        .WithTags("Purchasing")
        .RequirePermission(PermissionCodes.PurchasingCreate)
        .WithSummary("يرجّع صورة الفاتورة المحفوظة (WebP) لمسودة معيّنة - للعرض أثناء المراجعة.")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapPut("/api/v1/purchase-invoices/drafts/{draftId:guid}", async (
            Guid draftId,
            UpdatePurchaseInvoiceDraftRequest request,
            UpdatePurchaseInvoiceDraftHandler handler,
            CancellationToken cancellationToken) =>
        {
            var command = new UpdatePurchaseInvoiceDraftCommand(
                draftId, request.MatchedSupplierId, request.SupplierInvoiceReference, request.Items);
            var result = await handler.HandleAsync(command, cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("UpdatePurchaseInvoiceDraft")
        .WithTags("Purchasing")
        .RequirePermission(PermissionCodes.PurchasingCreate)
        .WithSummary("يحفظ تعديلات المراجع (مطابقة منتج/كمية/سعر/رقم دفعة) على مسودة لسا PendingReview.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapPost("/api/v1/purchase-invoices/drafts/{draftId:guid}/complete", async (
            Guid draftId,
            CompletePurchaseInvoiceDraftHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new CompletePurchaseInvoiceDraftCommand(draftId), cancellationToken);
            return result.ToHttpResult(response =>
                Results.Created($"/api/v1/purchase-invoices/{response.PurchaseInvoiceId}", response));
        })
        .WithName("CompletePurchaseInvoiceDraft")
        .WithTags("Purchasing")
        .RequirePermission(PermissionCodes.PurchasingCreate)
        .WithSummary("اعتماد نهائي - يرفض لو أي سطر لسا غير مطابَق بمنتج فعلي، وإلا يحوّل المسودة لفاتورة شراء حقيقية (تزيد المخزون فعليًا).")
        .Produces(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapDelete("/api/v1/purchase-invoices/drafts/{draftId:guid}", async (
            Guid draftId,
            DiscardPurchaseInvoiceDraftHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new DiscardPurchaseInvoiceDraftCommand(draftId), cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("DiscardPurchaseInvoiceDraft")
        .WithTags("Purchasing")
        .RequirePermission(PermissionCodes.PurchasingCreate)
        .WithSummary("يتجاهل مسودة فاتورة (لن تُعتمد أبدًا) - لا يحذف الصورة المحفوظة.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    public sealed record UpdatePurchaseInvoiceDraftRequest(
        Guid? MatchedSupplierId,
        string? SupplierInvoiceReference,
        IReadOnlyList<PurchaseInvoiceDraftItemDto> Items);
}
