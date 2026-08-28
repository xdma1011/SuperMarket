using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;

namespace SupermarketSystem.Application.Catalog.AddProductUnit;

public sealed record AddProductUnitCommand(
    Guid ProductId, string UnitName, decimal ConversionFactorToBase, string? BarcodeValue);

public sealed record AddProductUnitResponse(Guid ProductUnitId, Guid? BarcodeId);

/// <summary>
/// "الطرد بيتحوّل بالنهاية لحبة بالمخزون" — الوحدة الجديدة بتنضاف لمنتج
/// *موجود أصلًا*، بمعامل تحويل مباشر للوحدة الأساسية (كل منتج بيقرر
/// علاقته الخاصة، لا جدول وحدات قياس عام).
///
/// الباركود اختياري بنفس الطلب — الحالة الشائعة عمليًا: وحدة جديدة
/// (طرد/كرتونة) بتيجي مع باركود خاص فيها من الشركة المصنّعة مباشرة.
/// </summary>
public sealed class AddProductUnitHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ICatalogVersionService _catalogVersionService;

    public AddProductUnitHandler(IApplicationDbContext context, ICatalogVersionService catalogVersionService)
    {
        _context = context;
        _catalogVersionService = catalogVersionService;
    }

    public async Task<Result<AddProductUnitResponse>> HandleAsync(AddProductUnitCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.UnitName))
        {
            return Result.Failure<AddProductUnitResponse>(
                Error.Validation("ProductUnit.NameRequired", "اسم الوحدة مطلوب."));
        }

        if (command.ConversionFactorToBase <= 0)
        {
            return Result.Failure<AddProductUnitResponse>(
                Error.Validation("ProductUnit.ConversionFactorMustBePositive", "معامل التحويل يجب أن يكون موجبًا."));
        }

        var product = await _context.Products
            .Include(p => p.Units)
            .Include(p => p.Barcodes)
            .FirstOrDefaultAsync(p => p.Id == command.ProductId, cancellationToken);

        if (product is null)
        {
            return Result.Failure<AddProductUnitResponse>(
                Error.NotFound("ProductUnit.ProductNotFound", $"المنتج '{command.ProductId}' غير موجود."));
        }

        if (!string.IsNullOrWhiteSpace(command.BarcodeValue))
        {
            var barcodeTaken = await _context.ProductBarcodes.AsNoTracking()
                .AnyAsync(b => b.BarcodeValue == command.BarcodeValue, cancellationToken);
            if (barcodeTaken)
            {
                return Result.Failure<AddProductUnitResponse>(
                    Error.Conflict("ProductUnit.BarcodeTaken", $"الباركود '{command.BarcodeValue}' مسجَّل أصلًا لصنف آخر."));
            }
        }

        // وحدة جديدة أبدًا مش أساسية — الوحدة الأساسية تُحدَّد حصرًا وقت
        // إنشاء المنتج نفسه.
        var unit = product.AddUnit(command.UnitName.Trim(), command.ConversionFactorToBase, isBaseUnit: false);

        Guid? barcodeId = null;
        if (!string.IsNullOrWhiteSpace(command.BarcodeValue))
        {
            var barcode = product.AddBarcode(command.BarcodeValue.Trim(), unit.Id);
            barcodeId = barcode.Id;
        }

        await _context.SaveChangesAsync(cancellationToken);
        await _catalogVersionService.IncrementVersionAsync(cancellationToken);

        return Result.Success(new AddProductUnitResponse(unit.Id, barcodeId));
    }
}
