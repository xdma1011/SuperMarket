using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;

namespace SupermarketSystem.Application.Catalog.GetProductByBarcode;

public sealed record GetProductByBarcodeQuery(string BarcodeValue);

public sealed record ProductByBarcodeDto(Guid ProductId, string ProductName, Guid CategoryId, string CategoryName);

/// <summary>
/// كانت ناقصة بالكامل — أساس تدفّق "امسح الباركود أول": لازم نتحقق هل
/// الباركود موجود *قبل* ما نفتح نموذج منتج جديد، تفاديًا لتكرار بالغلط.
/// BarcodeValue فريدة بقاعدة البيانات أصلًا (Unique Index).
/// </summary>
public sealed class GetProductByBarcodeHandler
{
    private readonly IApplicationDbContext _context;

    public GetProductByBarcodeHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ProductByBarcodeDto?> HandleAsync(GetProductByBarcodeQuery query, CancellationToken cancellationToken)
    {
        return await _context.ProductBarcodes.AsNoTracking()
            .Where(b => b.BarcodeValue == query.BarcodeValue)
            .Join(_context.ProductUnits.AsNoTracking(), b => b.ProductUnitId, u => u.Id, (b, u) => u.ProductId)
            .Join(_context.Products.AsNoTracking(), productId => productId, p => p.Id,
                (productId, p) => new { p.Id, p.Name, p.CategoryId })
            .Join(_context.ProductCategories.AsNoTracking(), p => p.CategoryId, c => c.Id,
                (p, c) => new ProductByBarcodeDto(p.Id, p.Name, p.CategoryId, c.Name))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
