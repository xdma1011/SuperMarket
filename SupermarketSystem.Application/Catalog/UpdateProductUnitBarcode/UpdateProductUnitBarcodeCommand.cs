using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;

namespace SupermarketSystem.Application.Catalog.UpdateProductUnitBarcode;

/// <summary>BarcodeValue فاضٍ/null = حذف الباركود من الوحدة (تصير بلا باركود، لا خطأ - نفس فلسفة الحقول الاختيارية بالمشروع).</summary>
public sealed record UpdateProductUnitBarcodeCommand(Guid ProductId, Guid ProductUnitId, string? BarcodeValue);

/// <summary>
/// يعالج فجوة كانت موجودة: باركود الوحدة كان يُكتب بس لحظة الإنشاء/إضافة
/// وحدة جديدة، بلا أي طريقة تصححه لاحقًا لو انكتب غلط أو تغيّر من
/// المورّد. وحدة واحدة = باركود كنسي واحد بس (أول باركود مسجَّل لها لو
/// تعددت لسبب ما) - نفس الافتراض المتبع بكل مكان ثاني بالمشروع.
/// </summary>
public sealed class UpdateProductUnitBarcodeHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ICatalogVersionService _catalogVersionService;

    public UpdateProductUnitBarcodeHandler(IApplicationDbContext context, ICatalogVersionService catalogVersionService)
    {
        _context = context;
        _catalogVersionService = catalogVersionService;
    }

    public async Task<Result> HandleAsync(UpdateProductUnitBarcodeCommand command, CancellationToken cancellationToken)
    {
        var product = await _context.Products
            .Include(p => p.Units)
            .Include(p => p.Barcodes)
            .FirstOrDefaultAsync(p => p.Id == command.ProductId, cancellationToken);

        if (product is null)
        {
            return Result.Failure(Error.NotFound("ProductUnit.ProductNotFound", $"المنتج '{command.ProductId}' غير موجود."));
        }

        var unit = product.Units.FirstOrDefault(u => u.Id == command.ProductUnitId);
        if (unit is null)
        {
            return Result.Failure(Error.NotFound("ProductUnit.NotFound", "الوحدة غير موجودة لهذا المنتج."));
        }

        var existingBarcode = product.Barcodes.FirstOrDefault(b => b.ProductUnitId == command.ProductUnitId);
        var newValue = command.BarcodeValue?.Trim();

        if (string.IsNullOrWhiteSpace(newValue))
        {
            if (existingBarcode is not null)
            {
                product.RemoveBarcode(existingBarcode.Id);
                await _context.SaveChangesAsync(cancellationToken);
                await _catalogVersionService.IncrementVersionAsync(cancellationToken);
            }

            return Result.Success();
        }

        var barcodeTakenByAnother = await _context.ProductBarcodes.AsNoTracking()
            .AnyAsync(b => b.BarcodeValue == newValue && b.Id != (existingBarcode != null ? existingBarcode.Id : Guid.Empty), cancellationToken);

        if (barcodeTakenByAnother)
        {
            return Result.Failure(Error.Conflict("ProductUnit.BarcodeTaken", $"الباركود '{newValue}' مسجَّل أصلًا لصنف آخر."));
        }

        if (existingBarcode is not null)
        {
            existingBarcode.ChangeValue(newValue);
        }
        else
        {
            product.AddBarcode(newValue, command.ProductUnitId);
        }

        await _context.SaveChangesAsync(cancellationToken);
        await _catalogVersionService.IncrementVersionAsync(cancellationToken);

        return Result.Success();
    }
}
