using SupermarketSystem.API.Common;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Application.Sales.CompleteSale;
using SupermarketSystem.Application.Sales.GetCustomerDebts;
using SupermarketSystem.Application.Sales.GetSaleInvoiceById;
using SupermarketSystem.Application.Sales.GetSaleInvoices;
using SupermarketSystem.Application.Sales.RecordSaleInvoicePayment;
using SupermarketSystem.Application.Sales.RejectedSales;
using SupermarketSystem.Application.Sales.VoidSale;
using SupermarketSystem.Domain.Sales;

using SupermarketSystem.Application.Sales.CreditCustomer;

namespace SupermarketSystem.API.Endpoints;

public static class SalesEndpoints
{
    public static IEndpointRouteBuilder MapSalesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/sales").WithTags("Sales");

        group.MapPost("/", async (
            CompleteSaleCommand command,
            CompleteSaleHandler handler,
            IRejectedSaleRecorder rejectedSaleRecorder,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(command, cancellationToken);

            // بند 24: رفض نهائي بيتسجّل (بيعة الكاشير العالقة بالطابور ما لازم تضيع بصمت)، ونجاح بيقفل أي سجل رفض
            // سابق لنفس البيعة. كلاهما أفضل جهد ومنفصل عن معاملة البيع، وما بيغيّر الرد.
            if (result.IsFailure)
            {
                await rejectedSaleRecorder.RecordRejectionAsync(command, result.Error!, CancellationToken.None);
            }
            else
            {
                await rejectedSaleRecorder.MarkAcceptedAsync(command.ClientRequestId, CancellationToken.None);
            }

            return result.ToHttpResult(response =>
                // A replay returns 200, not 201: the resource was created by
                // the ORIGINAL request, not this one. A client retrying after
                // a network timeout gets the same sale back and can tell from
                // the status code that it did not ring up a second one.
                response.WasReplay
                    ? Results.Ok(response)
                    : Results.Created($"/api/v1/sales/{response.SaleInvoiceId}", response));
        })
        .WithName("CompleteSale")
        .RequirePermission(PermissionCodes.SalesCreate)
        .WithSummary("Completes a sale: decrements stock atomically, records the invoice, payments, stock movements and cash-drawer entries in one transaction.")
        .Produces<CompleteSaleResponse>(StatusCodes.Status201Created)
        .Produces<CompleteSaleResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{saleInvoiceId:guid}/void", async (
            Guid saleInvoiceId,
            VoidSaleRequest request,
            VoidSaleHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(
                new VoidSaleCommand(saleInvoiceId, request.Reason, request.Notes), cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("VoidSale")
        .RequirePermission(PermissionCodes.SalesVoid)
        .WithSummary("يلغي فاتورة بيع مكتملة: الفاتورة تبقى محفوظة بحالة Voided، ويُعكس المخزون والدفعات وحركات الدرج بمعاملة واحدة.")
        .Produces<VoidSaleResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/", async (
            int? pageNumber, int? pageSize, string? search, string? sortBy, string? sortDirection,
            Guid? branchId, DateTime? fromUtc, DateTime? toUtc, string? productSearch,
            Guid? cashierUserId, Guid? paymentMethodId,
            GetSaleInvoicesHandler handler,
            CancellationToken cancellationToken) =>
        {
            var paging = PagingBinder.Build(pageNumber, pageSize, search, sortBy, sortDirection);
            var result = await handler.HandleAsync(
                new GetSaleInvoicesQuery(paging, branchId, ToUtc(fromUtc), ToUtc(toUtc), productSearch, cashierUserId, paymentMethodId), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetSaleInvoices")
        .RequirePermission(PermissionCodes.SalesCreate)
        .WithSummary("قائمة/بحث فواتير البيع - أساس البحث عن فاتورة أصلية قبل أي عملية إرجاع.")
        .Produces<PagedResult<SaleInvoiceListItemDto>>(StatusCodes.Status200OK);

        group.MapGet("/credit-customer", async (string phone, LookupCreditCustomerHandler handler, CancellationToken cancellationToken) =>
                (await handler.HandleAsync(new LookupCreditCustomerQuery(phone), cancellationToken)).ToHttpResult())
            .WithName("LookupCreditCustomer")
            .RequirePermission(PermissionCodes.SalesCreate)
            .WithSummary("بيع بالدين من الكاشير: الزبون المسجّل برقمه (آخر 9 أرقام) ودينه الحالي.")
            .Produces<CreditCustomerDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/filter-options", async (Guid? branchId, GetSaleFilterOptionsHandler handler, CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(branchId, cancellationToken)))
            .WithName("GetSaleFilterOptions")
            .RequirePermission(PermissionCodes.SalesCreate)
            .WithSummary("خيارات فلاتر صفحة المبيعات: الكاشيرية اللي إلهم فواتير، وطرق الدفع.")
            .Produces<SaleFilterOptionsDto>(StatusCodes.Status200OK);

        group.MapGet("/{saleInvoiceId:guid}", async (
            Guid saleInvoiceId,
            GetSaleInvoiceByIdHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new GetSaleInvoiceByIdQuery(saleInvoiceId), cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("GetSaleInvoiceById")
        .RequirePermission(PermissionCodes.SalesCreate)
        .WithSummary("تفاصيل فاتورة بيع بأصنافها الكاملة - كل سطر بكميته القابلة للإرجاع (Quantity - QuantityReturned).")
        .Produces<SaleInvoiceDetailDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{saleInvoiceId:guid}/payments", async (
            Guid saleInvoiceId,
            RecordSaleInvoicePaymentRequest request,
            RecordSaleInvoicePaymentHandler handler,
            CancellationToken cancellationToken) =>
        {
            var command = new RecordSaleInvoicePaymentCommand(
                saleInvoiceId, request.PaymentMethodId, request.Amount,
                request.ExternalReference, request.ClientRequestId);
            var result = await handler.HandleAsync(command, cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("RecordSaleInvoicePayment")
        .RequirePermission(PermissionCodes.SalesCreate)
        .WithSummary("يسجّل دفعة لاحقة من زبون على فاتورة بيع بالدين - يقلّل الدين المتبقي (لا يتجاوز الإجمالي).")
        .Produces<RecordSaleInvoicePaymentResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        app.MapGet("/api/v1/sales/customer-debts", async (
            GetCustomerDebtsHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetCustomerDebts")
        .WithTags("Sales")
        .RequirePermission(PermissionCodes.ReportsView)
        .WithSummary("قديش إلنا عند كل زبون (بيع بالدين) + مجموع الديون الكلي.")
        .Produces<GetCustomerDebtsResponse>(StatusCodes.Status200OK);

        // بيعات رفضها السيرفر نهائيًا (بند 24) - برّا المجموعة عمدًا (§3.4: صلاحية مختلفة عن بقية المبيعات).
        // Returns.Review = صاحب المحل ومساعد الأدمن؛ الكاشير ما بيشوفها.
        app.MapGet("/api/v1/sales/rejected", async (
            int? pageNumber, int? pageSize, string? search, Guid? branchId, bool? includeResolved,
            GetRejectedSalesHandler handler,
            CancellationToken cancellationToken) =>
        {
            var paging = PagingBinder.Build(pageNumber, pageSize, search, null, null);
            return Results.Ok(await handler.HandleAsync(new GetRejectedSalesQuery(paging, branchId, OnlyOpen: includeResolved != true), cancellationToken));
        })
        .WithTags("Sales")
        .WithName("GetRejectedSales")
        .RequirePermission(PermissionCodes.ReturnsReview)
        .WithSummary("بيعات رفضها السيرفر نهائيًا (عالقة بطابور الكاشير) - المفتوحة افتراضيًا.")
        .Produces<PagedResult<RejectedSaleListItemDto>>(StatusCodes.Status200OK);

        app.MapGet("/api/v1/sales/rejected/{id:guid}", async (
            Guid id, GetRejectedSaleByIdHandler handler, CancellationToken cancellationToken) =>
            (await handler.HandleAsync(id, cancellationToken)).ToHttpResult())
        .WithTags("Sales")
        .WithName("GetRejectedSaleById")
        .RequirePermission(PermissionCodes.ReturnsReview)
        .WithSummary("تفاصيل بيعة مرفوضة: الأصناف والدفعات بأسمائها وسبب الرفض.")
        .Produces<RejectedSaleDetailsDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapPost("/api/v1/sales/rejected/{id:guid}/resolve", async (
            Guid id, ResolveRejectedSaleRequest request, ResolveRejectedSaleHandler handler, CancellationToken cancellationToken) =>
            (await handler.HandleAsync(new ResolveRejectedSaleCommand(id, request.Note), cancellationToken)).ToHttpResult())
        .WithTags("Sales")
        .WithName("ResolveRejectedSale")
        .RequirePermission(PermissionCodes.ReturnsReview)
        .WithSummary("تعليم بيعة مرفوضة 'تمت المعالجة' + ملاحظة. بلا أي أثر مالي أو مخزني.")
        .Produces<Guid>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    /// <summary>
    /// "2026-09-28T11:30:00Z" بيوصل Kind=Utc، بس بلا Z بيوصل Unspecified - بنعتبره UTC صراحة
    /// (التطبيقات بتبعت UTC دايمًا)، عشان المقارنة مع CreatedAtUtc ما تنزاح بفرق التوقيت.
    /// </summary>
    private static DateTime? ToUtc(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Local } v => v.ToUniversalTime(),
        { Kind: DateTimeKind.Unspecified } v => DateTime.SpecifyKind(v, DateTimeKind.Utc),
        { } v => v
    };

    /// <summary>SaleInvoiceId يجي من المسار لا من الجسم.</summary>
    public sealed record VoidSaleRequest(VoidReason Reason, string? Notes);

    /// <summary>SaleInvoiceId يجي من المسار لا من الجسم.</summary>
    public sealed record RecordSaleInvoicePaymentRequest(
        Guid PaymentMethodId, decimal Amount, string? ExternalReference, Guid ClientRequestId);
}

public sealed record ResolveRejectedSaleRequest(string? Note);
