using SupermarketSystem.API.Common;
using SupermarketSystem.Application.CashierSync.GetCatalogSyncPage;
using SupermarketSystem.Application.CashierSync.GetCatalogVersion;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;

namespace SupermarketSystem.API.Endpoints;

/// <summary>
/// Sales.Create عمدًا (لا صلاحية جديدة) — هذه الـendpoints موجودة
/// حصرًا لدعم تطبيق الكاشير الأوفلاين، ودور "كاشير" أصلًا عنده هذه
/// الصلاحية.
/// </summary>
public static class CashierSyncEndpoints
{
    public static IEndpointRouteBuilder MapCashierSyncEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/cashier-sync").WithTags("CashierSync").RequirePermission(PermissionCodes.SalesCreate);

        group.MapGet("/catalog-version", async (
            GetCatalogVersionHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetCatalogVersion")
        .WithSummary("رقم نسخة الكتالوج الحالي.")
        .Produces<CatalogVersionResponse>(StatusCodes.Status200OK);

        group.MapGet("/catalog-page", async (
            Guid branchId, int? pageNumber, int? pageSize,
            GetCatalogSyncPageHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(
                new GetCatalogSyncPageQuery(branchId, pageNumber ?? 1, pageSize ?? 200), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetCatalogSyncPage")
        .WithSummary("صفحة من الكتالوج الكامل لمزامنة الكاشير المحلي.")
        .Produces<PagedResult<CatalogSyncProductDto>>(StatusCodes.Status200OK);

        group.MapGet("/store-branding", async (
            ISettingsProvider settingsProvider,
            CancellationToken cancellationToken) =>
        {
            var storeName = await settingsProvider.GetStringAsync(StoreBrandingKeys.StoreName, null, cancellationToken);
            return Results.Ok(new StoreBrandingResponse(storeName));
        })
        .WithName("GetStoreBranding")
        .WithSummary("اسم المحل ليُطبع بأعلى فاتورة الكاشير الحرارية - يُخزَّن محليًا بالكاشير للطباعة أوفلاين.")
        .Produces<StoreBrandingResponse>(StatusCodes.Status200OK);

        group.MapGet("/payment-settings", async (
            ISettingsProvider settingsProvider,
            CancellationToken cancellationToken) =>
        {
            var exchangeRate = await settingsProvider.GetDecimalAsync(PaymentSettingsKeys.UsdToJodExchangeRate, 0.71m, cancellationToken);
            return Results.Ok(new PaymentSettingsResponse(exchangeRate));
        })
        .WithName("GetCashierPaymentSettings")
        .WithSummary("سعر تحويل الدولار للدينار - يُخزَّن محليًا بالكاشير لحساب الفكة أوفلاين.")
        .Produces<PaymentSettingsResponse>(StatusCodes.Status200OK);

        return app;
    }

    public sealed record StoreBrandingResponse(string? StoreName);
    public sealed record PaymentSettingsResponse(decimal UsdToJodExchangeRate);
}
