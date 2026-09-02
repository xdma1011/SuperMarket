using SupermarketSystem.API.Common;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Reviews.GetPendingReviews;
using SupermarketSystem.Application.Reviews.MarkPurchaseInvoiceItemReviewed;
using SupermarketSystem.Application.Reviews.MarkStockMovementReviewed;

namespace SupermarketSystem.API.Endpoints;

/// <summary>
/// نقطة عرض/إجراء موحّدة لكل شي بانتظار مراجعة إدارية. بيستخدم صلاحية
/// Returns.Review الموجودة أصلًا (بدل صلاحية جديدة).
/// </summary>
public static class ReviewsEndpoints
{
    public static IEndpointRouteBuilder MapReviewsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/reviews").WithTags("Reviews").RequirePermission(PermissionCodes.ReturnsReview);

        group.MapGet("/", async (
            GetPendingReviewsHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetPendingReviews")
        .WithSummary("كل شي بانتظار مراجعة إدارية - إرجاعات وضيافة تجاوزت الحد، بشكل موحّد.")
        .Produces<GetPendingReviewsResponse>(StatusCodes.Status200OK);

        group.MapPost("/stock-movements/{stockMovementId:guid}/mark-reviewed", async (
            Guid stockMovementId,
            MarkStockMovementReviewedHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new MarkStockMovementReviewedCommand(stockMovementId), cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("MarkStockMovementReviewed")
        .WithSummary("يعلّم حركة مخزون (ضيافة) كمُراجَعة.")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/purchase-invoice-items/{purchaseInvoiceItemId:guid}/mark-reviewed", async (
            Guid purchaseInvoiceItemId,
            MarkPurchaseInvoiceItemReviewedHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new MarkPurchaseInvoiceItemReviewedCommand(purchaseInvoiceItemId), cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("MarkPurchaseInvoiceItemReviewed")
        .WithSummary("يعلّم سطر فاتورة شراء (سعر مرتفع بشكل ملحوظ) كمُراجَع.")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }
}
