using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;

namespace SupermarketSystem.Application.Reporting.GetProductConsumptionLevels;

public static class ConsumptionLevelSettingsKeys
{
    public const string HighThreshold = "ConsumptionLevel.HighThreshold";
    public const string MediumThreshold = "ConsumptionLevel.MediumThreshold";
    public const string LowThreshold = "ConsumptionLevel.LowThreshold";
}

public enum ConsumptionLevel
{
    High = 1,
    Medium = 2,
    Low = 3,
    NearZero = 4
}

public sealed record GetProductConsumptionLevelsQuery(PagedRequest Paging, Guid BranchId, DateTime SinceUtc);

public sealed record ProductConsumptionItemDto(
    Guid ProductId,
    string ProductName,
    decimal QuantitySold,
    int LevelCode,
    string LevelTitle);

/// <summary>
/// توسيع لمفهوم "الأصناف الراكدة" (ثنائي) لتصنيف متدرّج — نفس الكمية
/// المباعة خلال الفترة، بس مقسَّمة لمستويات بحدود قابلة للتعديل من
/// الإعدادات.
/// </summary>
public sealed class GetProductConsumptionLevelsHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ISettingsProvider _settingsProvider;

    public GetProductConsumptionLevelsHandler(IApplicationDbContext context, ISettingsProvider settingsProvider)
    {
        _context = context;
        _settingsProvider = settingsProvider;
    }

    public async Task<PagedResult<ProductConsumptionItemDto>> HandleAsync(
        GetProductConsumptionLevelsQuery query, CancellationToken cancellationToken)
    {
        var paging = query.Paging.Normalized();

        var highThreshold = await _settingsProvider.GetDecimalAsync(ConsumptionLevelSettingsKeys.HighThreshold, 50m, cancellationToken);
        var mediumThreshold = await _settingsProvider.GetDecimalAsync(ConsumptionLevelSettingsKeys.MediumThreshold, 15m, cancellationToken);
        var lowThreshold = await _settingsProvider.GetDecimalAsync(ConsumptionLevelSettingsKeys.LowThreshold, 1m, cancellationToken);

        var soldQuantityByProduct = _context.SaleInvoiceItems.AsNoTracking()
            .Join(_context.SaleInvoices.AsNoTracking(),
                i => i.SaleInvoiceId, s => s.Id, (i, s) => new { i.ProductId, i.Quantity, s.BranchId, s.CreatedAtUtc })
            .Where(x => x.BranchId == query.BranchId && x.CreatedAtUtc >= query.SinceUtc)
            .GroupBy(x => x.ProductId)
            .Select(g => new { ProductId = g.Key, QuantitySold = g.Sum(x => x.Quantity) });

        var withSales = _context.ProductBranches.AsNoTracking()
            .Where(pb => pb.BranchId == query.BranchId && pb.IsAvailableForSale)
            .Join(_context.Products.AsNoTracking(), pb => pb.ProductId, p => p.Id, (pb, p) => new { pb.ProductId, ProductName = p.Name })
            .GroupJoin(soldQuantityByProduct, x => x.ProductId, s => s.ProductId, (x, sales) => new { x.ProductId, x.ProductName, sales })
            .SelectMany(x => x.sales.DefaultIfEmpty(), (x, s) => new
            {
                x.ProductId,
                x.ProductName,
                QuantitySold = s != null ? s.QuantitySold : 0m
            });

        if (!string.IsNullOrWhiteSpace(paging.Search))
        {
            var pattern = $"%{paging.Search.Trim()}%";
            withSales = withSales.Where(x => EF.Functions.Like(x.ProductName, pattern));
        }

        var totalCount = await withSales.CountAsync(cancellationToken);

        var page = await withSales
            .OrderByDescending(x => x.QuantitySold)
            .ThenBy(x => x.ProductName)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .ToListAsync(cancellationToken);

        var items = page.Select(x => ToDto(x.ProductId, x.ProductName, x.QuantitySold, highThreshold, mediumThreshold, lowThreshold)).ToList();

        return new PagedResult<ProductConsumptionItemDto>(items, totalCount, paging.PageNumber, paging.PageSize);
    }

    private static ProductConsumptionItemDto ToDto(
        Guid productId, string productName, decimal quantitySold,
        decimal highThreshold, decimal mediumThreshold, decimal lowThreshold)
    {
        var level = quantitySold >= highThreshold ? ConsumptionLevel.High
            : quantitySold >= mediumThreshold ? ConsumptionLevel.Medium
            : quantitySold >= lowThreshold ? ConsumptionLevel.Low
            : ConsumptionLevel.NearZero;

        var title = level switch
        {
            ConsumptionLevel.High => "عالي",
            ConsumptionLevel.Medium => "متوسط",
            ConsumptionLevel.Low => "ضعيف",
            _ => "شبه معدوم"
        };

        return new ProductConsumptionItemDto(productId, productName, quantitySold, (int)level, title);
    }
}
