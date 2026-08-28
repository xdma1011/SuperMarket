using SupermarketSystem.API.Common;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Application.Purchasing.CompletePurchaseInvoice;
using SupermarketSystem.Application.Purchasing.GetPurchaseInvoices;
using SupermarketSystem.Application.Purchasing.GetSupplierDebts;
using SupermarketSystem.Application.Purchasing.RecordPurchaseInvoicePayment;

namespace SupermarketSystem.API.Endpoints;

/// <summary>
/// Suppliers live in SupplierEndpoints.cs (mapped separately in Program.cs) —
/// this group only adds purchase-invoice completion, to avoid two files both
/// registering "/api/v1/suppliers" and colliding at route-mapping time.
/// </summary>
public static class PurchasingEndpoints
{
    public static IEndpointRouteBuilder MapPurchasingEndpoints(this IEndpointRouteBuilder app)
    {
        var purchaseInvoices = app.MapGroup("/api/v1/purchase-invoices").WithTags("Purchasing").RequirePermission(PermissionCodes.PurchasingCreate);

        purchaseInvoices.MapPost("/", async (
            CompletePurchaseInvoiceCommand command,
            CompletePurchaseInvoiceHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(command, cancellationToken);
            return result.ToHttpResult(response =>
                Results.Created($"/api/v1/purchase-invoices/{response.PurchaseInvoiceId}", response));
        })
        .WithName("CompletePurchaseInvoice")
        .WithSummary("Records a received purchase: reserves the invoice number, then in one transaction writes the invoice, a PurchaseIn StockMovement per item, and increases Stock.")
        .Produces<CompletePurchaseInvoiceResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);

        purchaseInvoices.MapGet("/", async (
            int? pageNumber, int? pageSize, string? search, string? sortBy, string? sortDirection,
            Guid? branchId,
            GetPurchaseInvoicesHandler handler,
            CancellationToken cancellationToken) =>
        {
            var paging = PagingBinder.Build(pageNumber, pageSize, search, sortBy, sortDirection);
            var result = await handler.HandleAsync(new GetPurchaseInvoicesQuery(paging, branchId), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetPurchaseInvoices")
        .Produces<PagedResult<PurchaseInvoiceListItemDto>>(StatusCodes.Status200OK);

        purchaseInvoices.MapPost("/{purchaseInvoiceId:guid}/payments", async (
            Guid purchaseInvoiceId,
            RecordPurchaseInvoicePaymentRequest request,
            RecordPurchaseInvoicePaymentHandler handler,
            CancellationToken cancellationToken) =>
        {
            var command = new RecordPurchaseInvoicePaymentCommand(
                purchaseInvoiceId, request.PaymentMethodId, request.Amount,
                request.ExternalReference, request.ClientRequestId);
            var result = await handler.HandleAsync(command, cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("RecordPurchaseInvoicePayment")
        .WithSummary("يسجّل دفعة فعلية للمورد على فاتورة شراء - يقلّل الدين المتبقي.")
        .Produces<RecordPurchaseInvoicePaymentResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        // خارج مجموعة purchaseInvoices عمدًا (لا purchaseInvoices.MapGet) —
        // المجموعة عندها Purchasing.Create مطبَّقة كفلتر أصلًا، والفلاتر
        // بتتراكم (AND) لا تتجاوز بعض. لو نادينا RequirePermission هون
        // وهو جوّا المجموعة، كان رح يتطلب Purchasing.Create وReports.View
        // معًا، بينما القصد Reports.View لحالها (هذا تقرير، لا عملية شراء).
        app.MapGet("/api/v1/purchase-invoices/supplier-debts", async (
            GetSupplierDebtsHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetSupplierDebts")
        .WithTags("Purchasing")
        .RequirePermission(PermissionCodes.ReportsView)
        .WithSummary("قديش عليك لكل مورد (فواتير Received فقط) + مجموع الديون الكلي.")
        .Produces<GetSupplierDebtsResponse>(StatusCodes.Status200OK);

        return app;
    }

    /// <summary>PurchaseInvoiceId يجي من المسار لا من الجسم.</summary>
    public sealed record RecordPurchaseInvoicePaymentRequest(
        Guid PaymentMethodId, decimal Amount, string? ExternalReference, Guid ClientRequestId);
}
