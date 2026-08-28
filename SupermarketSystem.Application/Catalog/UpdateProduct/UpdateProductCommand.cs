using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;

namespace SupermarketSystem.Application.Catalog.UpdateProduct;

/// <summary>
/// IsBatchTracked وIsComplimentaryAllowed عمدًا خارج هذا الأمر —
/// الأول قرار بنيوي (تغييره بعد إنشاء دفعات فعلية يكسر الافتراضات
/// المبنية عليه بكل مكان تاني)، والثاني له endpoint مخصص فعلًا
/// (SetProductComplimentaryAllowed).
/// </summary>
public sealed record UpdateProductCommand(
    Guid ProductId,
    string Name,
    Guid CategoryId,
    decimal? SuggestedRetailPrice,
    int? ExpectedShelfLifeDays);

public sealed class UpdateProductHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ICatalogVersionService _catalogVersionService;

    public UpdateProductHandler(IApplicationDbContext context, ICatalogVersionService catalogVersionService)
    {
        _context = context;
        _catalogVersionService = catalogVersionService;
    }

    public async Task<Result> HandleAsync(UpdateProductCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
        {
            return Result.Failure(Error.Validation("Product.NameRequired", "اسم المنتج مطلوب."));
        }

        var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == command.ProductId, cancellationToken);

        if (product is null)
        {
            return Result.Failure(Error.NotFound("Product.NotFound", $"المنتج '{command.ProductId}' غير موجود."));
        }

        var categoryExists = await _context.ProductCategories.AsNoTracking()
            .AnyAsync(c => c.Id == command.CategoryId, cancellationToken);
        if (!categoryExists)
        {
            return Result.Failure(Error.NotFound("Product.CategoryNotFound", $"التصنيف '{command.CategoryId}' غير موجود."));
        }

        product.Rename(command.Name.Trim());
        product.ChangeCategory(command.CategoryId);
        product.SetSuggestedRetailPrice(command.SuggestedRetailPrice);
        product.SetExpectedShelfLifeDays(command.ExpectedShelfLifeDays);

        await _context.SaveChangesAsync(cancellationToken);
        await _catalogVersionService.IncrementVersionAsync(cancellationToken);

        return Result.Success();
    }
}
