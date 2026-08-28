using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;

namespace SupermarketSystem.Application.Catalog.SetProductComplimentaryAllowed;

public sealed record SetProductComplimentaryAllowedCommand(Guid ProductId, bool Allowed);

/// <summary>
/// أصغر عملية تعديل ممكنة عمدًا — سطر واحد يتبدّل، لا نموذج تعديل عام
/// للمنتج (اسم، سعر، تصنيف، إلخ — غير موجود بعد). أول تعديل حقيقي بكل
/// النظام، اللي كان قبلها إنشاء فقط بكل مكان.
/// </summary>
public sealed class SetProductComplimentaryAllowedHandler
{
    private readonly IApplicationDbContext _context;

    public SetProductComplimentaryAllowedHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result> HandleAsync(SetProductComplimentaryAllowedCommand command, CancellationToken cancellationToken)
    {
        var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == command.ProductId, cancellationToken);

        if (product is null)
        {
            return Result.Failure(Error.NotFound("Product.NotFound", $"المنتج '{command.ProductId}' غير موجود."));
        }

        product.SetComplimentaryAllowed(command.Allowed);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
