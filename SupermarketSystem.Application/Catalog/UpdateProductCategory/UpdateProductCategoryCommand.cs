using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;

namespace SupermarketSystem.Application.Catalog.UpdateProductCategory;

public sealed record UpdateProductCategoryCommand(Guid CategoryId, string Name);

public sealed class UpdateProductCategoryHandler
{
    private readonly IApplicationDbContext _context;

    public UpdateProductCategoryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result> HandleAsync(UpdateProductCategoryCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
        {
            return Result.Failure(Error.Validation("ProductCategory.NameRequired", "اسم التصنيف مطلوب."));
        }

        var category = await _context.ProductCategories.FirstOrDefaultAsync(c => c.Id == command.CategoryId, cancellationToken);

        if (category is null)
        {
            return Result.Failure(Error.NotFound("ProductCategory.NotFound", $"التصنيف '{command.CategoryId}' غير موجود."));
        }

        category.Rename(command.Name.Trim());
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
