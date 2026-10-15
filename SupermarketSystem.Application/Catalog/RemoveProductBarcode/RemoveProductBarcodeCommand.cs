using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;

namespace SupermarketSystem.Application.Catalog.RemoveProductBarcode;

public sealed record RemoveProductBarcodeCommand(Guid ProductId, Guid BarcodeId);

/// <summary>
/// حذف باركود (إضافي أو أساسي) من صنف - مثلًا باركود نسخة ما عاد بتنباع. الصنف والمبيعات القديمة ما بتتأثر (الفاتورة بتحمل
/// المنتج والوحدة مش الباركود). بينزيد رقم نسخة الكتالوج فالكاشير بيسحب التغيير.
/// </summary>
public sealed class RemoveProductBarcodeHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ICatalogVersionService _catalogVersionService;

    public RemoveProductBarcodeHandler(IApplicationDbContext context, ICatalogVersionService catalogVersionService)
    {
        _context = context;
        _catalogVersionService = catalogVersionService;
    }

    public async Task<Result> HandleAsync(RemoveProductBarcodeCommand command, CancellationToken cancellationToken)
    {
        var barcode = await _context.ProductBarcodes
            .FirstOrDefaultAsync(b => b.Id == command.BarcodeId && b.ProductId == command.ProductId, cancellationToken);
        if (barcode is null)
        {
            return Result.Failure(Error.NotFound("ProductBarcode.NotFound", "الباركود غير موجود لهذا الصنف."));
        }

        _context.ProductBarcodes.Remove(barcode);
        await _context.SaveChangesAsync(cancellationToken);
        await _catalogVersionService.IncrementVersionAsync(cancellationToken);
        return Result.Success();
    }
}
