using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Catalog;

namespace SupermarketSystem.Application.Catalog.CreateProductCategory;

public sealed record CreateProductCategoryCommand(string Name, Guid? ParentCategoryId);

public sealed record CreateProductCategoryResponse(Guid CategoryId, string Name);

public static class CreateProductCategoryValidator
{
    private const int MaxNameLength = 200;

    public static Error? Validate(CreateProductCategoryCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
        {
            return Error.Validation("ProductCategory.NameRequired", "Category name is required.");
        }

        if (command.Name.Length > MaxNameLength)
        {
            return Error.Validation("ProductCategory.NameTooLong", $"Category name cannot exceed {MaxNameLength} characters.");
        }

        return null;
    }
}

public sealed class CreateProductCategoryHandler
{
    private readonly IApplicationDbContext _context;

    public CreateProductCategoryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<CreateProductCategoryResponse>> HandleAsync(
        CreateProductCategoryCommand command,
        CancellationToken cancellationToken)
    {
        var validationError = CreateProductCategoryValidator.Validate(command);
        if (validationError is not null)
        {
            return Result.Failure<CreateProductCategoryResponse>(validationError);
        }

        if (command.ParentCategoryId is { } parentId)
        {
            // AsNoTracking: existence check only, never mutated here.
            var parentExists = await _context.ProductCategories
                .AsNoTracking()
                .AnyAsync(c => c.Id == parentId, cancellationToken);

            if (!parentExists)
            {
                return Result.Failure<CreateProductCategoryResponse>(
                    Error.NotFound("ProductCategory.ParentNotFound", $"Parent category '{parentId}' was not found."));
            }
        }

        var category = new ProductCategory(command.Name.Trim(), command.ParentCategoryId);
        _context.ProductCategories.Add(category);

        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(new CreateProductCategoryResponse(category.Id, category.Name));
    }
}
