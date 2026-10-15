using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;

namespace SupermarketSystem.Application.Catalog.AddProductBarcode;

public sealed record AddProductBarcodeCommand(Guid ProductId, Guid ProductUnitId, string BarcodeValue);

public sealed record AddProductBarcodeResponse(Guid BarcodeId);

/// <summary>
/// باركود إضافي لنفس الوحدة (9/10/2026): نفس الصنف بيجي بباركودات مختلفة حسب بلد التصنيع أو الموزّع أو الطبعة
/// (باونتي/سنكرز مستورد من بلدين = باركودين)، وكلهم نفس الصنف بنفس السعر والمخزون والعروض. قبل هيك الوحدة كان إلها باركود
/// واحد بس (<c>UpdateProductUnitBarcode</c>: تعديل/حذف). الباركود الأول للوحدة هو "الأساسي" (اللي بيبين بالقائمة)،
/// والباقي إضافي. فريد على مستوى النظام كله (نفس قاعدة بقية مسارات الباركود). بينزيد رقم نسخة الكتالوج فالكاشير بيسحبه.
/// </summary>
public sealed class AddProductBarcodeHandler
{
    public const int MaxBarcodeLength = 100;

    private readonly IApplicationDbContext _context;
    private readonly ICatalogVersionService _catalogVersionService;

    public AddProductBarcodeHandler(IApplicationDbContext context, ICatalogVersionService catalogVersionService)
    {
        _context = context;
        _catalogVersionService = catalogVersionService;
    }

    public async Task<Result<AddProductBarcodeResponse>> HandleAsync(AddProductBarcodeCommand command, CancellationToken cancellationToken)
    {
        var value = command.BarcodeValue?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Failure<AddProductBarcodeResponse>(Error.Validation("ProductBarcode.ValueRequired", "الباركود مطلوب."));
        }

        if (value.Length > MaxBarcodeLength)
        {
            return Result.Failure<AddProductBarcodeResponse>(
                Error.Validation("ProductBarcode.TooLong", $"الباركود أطول من {MaxBarcodeLength} حرف."));
        }

        var product = await _context.Products.Include(p => p.Units)
            .FirstOrDefaultAsync(p => p.Id == command.ProductId, cancellationToken);
        if (product is null)
        {
            return Result.Failure<AddProductBarcodeResponse>(Error.NotFound("ProductBarcode.ProductNotFound", "الصنف غير موجود."));
        }

        if (product.Units.All(u => u.Id != command.ProductUnitId))
        {
            return Result.Failure<AddProductBarcodeResponse>(Error.NotFound("ProductBarcode.UnitNotFound", "الوحدة غير موجودة لهذا الصنف."));
        }

        var takenBy = await _context.ProductBarcodes.AsNoTracking()
            .Where(b => b.BarcodeValue == value)
            .Select(b => b.ProductId)
            .FirstOrDefaultAsync(cancellationToken);
        if (takenBy != Guid.Empty)
        {
            var message = takenBy == command.ProductId
                ? $"الباركود '{value}' مسجَّل أصلًا على هذا الصنف."
                : $"الباركود '{value}' مسجَّل أصلًا لصنف آخر.";
            return Result.Failure<AddProductBarcodeResponse>(Error.Conflict("ProductUnit.BarcodeTaken", message));
        }

        // Add صريح (نفس فخ UpdateProductUnitBarcode): بلاه EF بيعتبرها Modified لأنها وصلت عبر navigation على Product موجود.
        var barcode = product.AddBarcode(value, command.ProductUnitId);
        _context.ProductBarcodes.Add(barcode);
        await _context.SaveChangesAsync(cancellationToken);
        await _catalogVersionService.IncrementVersionAsync(cancellationToken);

        return Result.Success(new AddProductBarcodeResponse(barcode.Id));
    }
}
