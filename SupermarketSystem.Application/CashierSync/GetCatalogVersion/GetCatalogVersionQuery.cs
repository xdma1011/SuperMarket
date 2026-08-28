using SupermarketSystem.Application.Common.Interfaces;

namespace SupermarketSystem.Application.CashierSync.GetCatalogVersion;

public sealed record CatalogVersionResponse(long Version);

/// <summary>
/// أخف استعلام بكل النظام عمدًا — رقم واحد بس. تطبيق الكاشير بيناديه
/// بشكل متكرر ليقرر هل يحتاج يسحب تحديث كامل للكتالوج المحلي.
/// </summary>
public sealed class GetCatalogVersionHandler
{
    private readonly ICatalogVersionService _catalogVersionService;

    public GetCatalogVersionHandler(ICatalogVersionService catalogVersionService)
    {
        _catalogVersionService = catalogVersionService;
    }

    public async Task<CatalogVersionResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var version = await _catalogVersionService.GetCurrentVersionAsync(cancellationToken);
        return new CatalogVersionResponse(version);
    }
}
