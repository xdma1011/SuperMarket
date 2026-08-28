using SupermarketSystem.API.Common;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Application.Reporting.GetBestCashiers;
using SupermarketSystem.Application.Reporting.GetBestCustomers;
using SupermarketSystem.Application.Reporting.GetCurrentCapitalValue;
using SupermarketSystem.Application.Reporting.GetManualDiscounts;
using SupermarketSystem.Application.Reporting.GetNegativeStock;
using SupermarketSystem.Application.Reporting.GetRecentReturns;
using SupermarketSystem.Application.Reporting.GetRecentReturnedItems;
using SupermarketSystem.Application.Reporting.GetReorderNeededProducts;
using SupermarketSystem.Application.Reporting.GetReturnFrequencyByProduct;
using SupermarketSystem.Application.Reporting.GetSalesSummary;
using SupermarketSystem.Application.Reporting.GetProductConsumptionLevels;
using SupermarketSystem.Application.Reporting.GetStagnantProducts;
using SupermarketSystem.Application.Reporting.GetSupplierPriceComparison;
using SupermarketSystem.Application.Reporting.GetVoidedSales;

namespace SupermarketSystem.API.Endpoints;

/// <summary>
/// Management-visibility queries (Architecture Review §14). Every one of
/// these is read-only and surfaces facts for a human to review — none of
/// them blocks, scores, or accuses. This is the "review later" half of
/// "allow → complete → record → classify → review".
/// </summary>
public static class ReportingEndpoints
{
    public static IEndpointRouteBuilder MapReportingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/reports").WithTags("Reporting").RequirePermission(PermissionCodes.ReportsView);

        group.MapGet("/returns/recent", async (
            int? pageNumber,
            int? pageSize,
            string? search,
            string? sortBy,
            string? sortDirection,
            Guid? branchId,
            DateTime? fromUtc,
            DateTime? toUtc,
            GetRecentReturnsHandler handler,
            CancellationToken cancellationToken) =>
        {
            var paging = PagingBinder.Build(pageNumber, pageSize, search, sortBy, sortDirection);
            var result = await handler.HandleAsync(new GetRecentReturnsQuery(paging, branchId, fromUtc, toUtc), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetRecentReturns")
        .Produces<PagedResult<RecentReturnItemDto>>(StatusCodes.Status200OK);

        group.MapGet("/sales/voided", async (
            int? pageNumber,
            int? pageSize,
            string? search,
            string? sortBy,
            string? sortDirection,
            Guid? branchId,
            DateTime? fromUtc,
            DateTime? toUtc,
            GetVoidedSalesHandler handler,
            CancellationToken cancellationToken) =>
        {
            var paging = PagingBinder.Build(pageNumber, pageSize, search, sortBy, sortDirection);
            var result = await handler.HandleAsync(new GetVoidedSalesQuery(paging, branchId, fromUtc, toUtc), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetVoidedSales")
        .Produces<PagedResult<VoidedSaleItemDto>>(StatusCodes.Status200OK);

        // fromUtc/toUtc stay non-nullable here, deliberately — a return-
        // frequency window with no date range doesn't mean anything, so this
        // pair is a genuinely required pair of query parameters, unrelated
        // to the [AsParameters]/PagingBinder fix.
        group.MapGet("/returns/frequency-by-product", async (
            int? pageNumber,
            int? pageSize,
            string? search,
            string? sortBy,
            string? sortDirection,
            Guid? branchId,
            DateTime fromUtc,
            DateTime toUtc,
            GetReturnFrequencyByProductHandler handler,
            CancellationToken cancellationToken) =>
        {
            var paging = PagingBinder.Build(pageNumber, pageSize, search, sortBy, sortDirection);
            var result = await handler.HandleAsync(
                new GetReturnFrequencyByProductQuery(paging, branchId, fromUtc, toUtc), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetReturnFrequencyByProduct")
        .WithSummary("Surfaces products with several returns in the given window — for informational review, not automated accusation.")
        .Produces<PagedResult<ReturnFrequencyItemDto>>(StatusCodes.Status200OK);

        group.MapGet("/discounts/manual", async (
            int? pageNumber,
            int? pageSize,
            string? search,
            string? sortBy,
            string? sortDirection,
            Guid? branchId,
            DateTime? fromUtc,
            DateTime? toUtc,
            GetManualDiscountsHandler handler,
            CancellationToken cancellationToken) =>
        {
            var paging = PagingBinder.Build(pageNumber, pageSize, search, sortBy, sortDirection);
            var result = await handler.HandleAsync(new GetManualDiscountsQuery(paging, branchId, fromUtc, toUtc), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetManualDiscounts")
        .Produces<PagedResult<ManualDiscountItemDto>>(StatusCodes.Status200OK);

        // تقرير الأصناف اللي رصيدها نزل تحت الصفر — مرتبط بإعداد
        // Inventory.AllowNegativeStock. مراجعة إدارية فقط، بلا أي تأثير
        // على البيع نفسه.
        group.MapGet("/inventory/negative-stock", async (
            int? pageNumber,
            int? pageSize,
            string? search,
            string? sortBy,
            string? sortDirection,
            Guid? branchId,
            GetNegativeStockHandler handler,
            CancellationToken cancellationToken) =>
        {
            var paging = PagingBinder.Build(pageNumber, pageSize, search, sortBy, sortDirection);
            var result = await handler.HandleAsync(new GetNegativeStockQuery(paging, branchId), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetNegativeStock")
        .WithSummary("Products whose Stock balance went negative because a sale was allowed to proceed despite insufficient system stock.")
        .Produces<PagedResult<NegativeStockItemDto>>(StatusCodes.Status200OK);

        // ملخص مبيعات لفترة، مع مقارنة اختيارية بفترة تانية بنفس الطلب.
        group.MapGet("/sales/summary", async (
            Guid? branchId,
            DateTime fromUtc,
            DateTime toUtc,
            DateTime? compareFromUtc,
            DateTime? compareToUtc,
            GetSalesSummaryHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(
                new GetSalesSummaryQuery(branchId, fromUtc, toUtc, compareFromUtc, compareToUtc), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetSalesSummary")
        .Produces<GetSalesSummaryResponse>(StatusCodes.Status200OK);

        group.MapGet("/cashiers/best", async (
            int? pageNumber, int? pageSize, string? search, string? sortBy, string? sortDirection,
            Guid? branchId, DateTime fromUtc, DateTime toUtc,
            GetBestCashiersHandler handler,
            CancellationToken cancellationToken) =>
        {
            var paging = PagingBinder.Build(pageNumber, pageSize, search, sortBy, sortDirection);
            var result = await handler.HandleAsync(new GetBestCashiersQuery(paging, branchId, fromUtc, toUtc), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetBestCashiers")
        .Produces<PagedResult<BestCashierItemDto>>(StatusCodes.Status200OK);

        group.MapGet("/customers/best", async (
            int? pageNumber, int? pageSize, string? search, string? sortBy, string? sortDirection,
            Guid? branchId, DateTime fromUtc, DateTime toUtc,
            GetBestCustomersHandler handler,
            CancellationToken cancellationToken) =>
        {
            var paging = PagingBinder.Build(pageNumber, pageSize, search, sortBy, sortDirection);
            var result = await handler.HandleAsync(new GetBestCustomersQuery(paging, branchId, fromUtc, toUtc), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetBestCustomers")
        .Produces<PagedResult<BestCustomerItemDto>>(StatusCodes.Status200OK);

        group.MapGet("/products/stagnant", async (
            int? pageNumber, int? pageSize, string? search, string? sortBy, string? sortDirection,
            Guid branchId, DateTime sinceUtc,
            GetStagnantProductsHandler handler,
            CancellationToken cancellationToken) =>
        {
            var paging = PagingBinder.Build(pageNumber, pageSize, search, sortBy, sortDirection);
            var result = await handler.HandleAsync(new GetStagnantProductsQuery(paging, branchId, sinceUtc), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetStagnantProducts")
        .Produces<PagedResult<StagnantProductItemDto>>(StatusCodes.Status200OK);

        group.MapGet("/products/consumption-levels", async (
            int? pageNumber, int? pageSize, string? search, string? sortBy, string? sortDirection,
            Guid branchId, DateTime sinceUtc,
            GetProductConsumptionLevelsHandler handler,
            CancellationToken cancellationToken) =>
        {
            var paging = PagingBinder.Build(pageNumber, pageSize, search, sortBy, sortDirection);
            var result = await handler.HandleAsync(new GetProductConsumptionLevelsQuery(paging, branchId, sinceUtc), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetProductConsumptionLevels")
        .WithSummary("تصنيف الأصناف حسب مستوى الاستهلاك (عالي/متوسط/ضعيف/شبه معدوم) - حدود قابلة للتعديل من الإعدادات.")
        .Produces<PagedResult<ProductConsumptionItemDto>>(StatusCodes.Status200OK);

        group.MapGet("/products/reorder-needed", async (
            int? pageNumber, int? pageSize, string? search, string? sortBy, string? sortDirection,
            Guid branchId,
            GetReorderNeededProductsHandler handler,
            CancellationToken cancellationToken) =>
        {
            var paging = PagingBinder.Build(pageNumber, pageSize, search, sortBy, sortDirection);
            var result = await handler.HandleAsync(new GetReorderNeededProductsQuery(paging, branchId), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetReorderNeededProducts")
        .Produces<PagedResult<ReorderNeededItemDto>>(StatusCodes.Status200OK);

        group.MapGet("/suppliers/price-comparison", async (
            int? pageNumber, int? pageSize, string? search, string? sortBy, string? sortDirection,
            Guid productId, DateTime? fromUtc, DateTime? toUtc,
            GetSupplierPriceComparisonHandler handler,
            CancellationToken cancellationToken) =>
        {
            var paging = PagingBinder.Build(pageNumber, pageSize, search, sortBy, sortDirection);
            var result = await handler.HandleAsync(
                new GetSupplierPriceComparisonQuery(paging, productId, fromUtc, toUtc), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetSupplierPriceComparison")
        .Produces<PagedResult<SupplierPriceComparisonItemDto>>(StatusCodes.Status200OK);

        group.MapGet("/returns/recent-items", async (
            int? pageNumber, int? pageSize, string? search, string? sortBy, string? sortDirection,
            Guid? branchId, DateTime? fromUtc, DateTime? toUtc,
            GetRecentReturnedItemsHandler handler,
            CancellationToken cancellationToken) =>
        {
            var paging = PagingBinder.Build(pageNumber, pageSize, search, sortBy, sortDirection);
            var result = await handler.HandleAsync(
                new GetRecentReturnedItemsQuery(paging, branchId, fromUtc, toUtc), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetRecentReturnedItems")
        .WithSummary("آخر الأصناف المرتجعة على مستوى السطر (لا الفاتورة)، مرتبة زمنيًا — أساس لجرد مفاجئ على آخر المرتجعات.")
        .Produces<PagedResult<RecentReturnedItemDto>>(StatusCodes.Status200OK);

        group.MapGet("/inventory/capital-value", async (
            int? pageNumber, int? pageSize, string? search, string? sortBy, string? sortDirection,
            Guid? branchId,
            GetCurrentCapitalValueHandler handler,
            CancellationToken cancellationToken) =>
        {
            var paging = PagingBinder.Build(pageNumber, pageSize, search, sortBy, sortDirection);
            var result = await handler.HandleAsync(new GetCurrentCapitalValueQuery(paging, branchId), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetCurrentCapitalValue")
        .WithSummary("قيمة رأس المال الحالي بالمخزون - متوسط مرجّح للتكلفة × الكمية الحالية.")
        .Produces<GetCurrentCapitalValueResponse>(StatusCodes.Status200OK);

        return app;
    }
}
