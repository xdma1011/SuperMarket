using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Catalog;

namespace SupermarketSystem.Application.Catalog.CreateProductBranch;

public sealed record CreateProductBranchCommand(
    Guid ProductId,
    Guid BranchId,
    decimal SellingPrice,
    decimal? MinimumStock,
    decimal? MaximumStock);

public sealed record CreateProductBranchResponse(Guid ProductBranchId, decimal SellingPrice);

public static class CreateProductBranchValidator
{
    public static Error? Validate(CreateProductBranchCommand command)
    {
        if (command.ProductId == Guid.Empty)
        {
            return Error.Validation("ProductBranch.ProductRequired", "A product is required.");
        }

        if (command.BranchId == Guid.Empty)
        {
            return Error.Validation("ProductBranch.BranchRequired", "A branch is required.");
        }

        if (command.SellingPrice < 0)
        {
            return Error.Validation("ProductBranch.SellingPriceNegative", "Selling price cannot be negative.");
        }

        if (command.MinimumStock is < 0 || command.MaximumStock is < 0)
        {
            return Error.Validation("ProductBranch.StockThresholdNegative", "Stock thresholds cannot be negative.");
        }

        if (command.MinimumStock is not null && command.MaximumStock is not null
            && command.MinimumStock > command.MaximumStock)
        {
            return Error.Validation("ProductBranch.MinExceedsMax", "Minimum stock cannot exceed maximum stock.");
        }

        return null;
    }
}

/// <summary>
/// A product is not sellable at a branch until this row exists — there is no
/// implicit fallback price (Architecture Review §1/§2 v2). This is the
/// explicit "onboard this product to this branch" action.
/// </summary>
public sealed class CreateProductBranchHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ICatalogVersionService _catalogVersionService;

    public CreateProductBranchHandler(IApplicationDbContext context, ICatalogVersionService catalogVersionService)
    {
        _context = context;
        _catalogVersionService = catalogVersionService;
    }

    public async Task<Result<CreateProductBranchResponse>> HandleAsync(
        CreateProductBranchCommand command,
        CancellationToken cancellationToken)
    {
        var validationError = CreateProductBranchValidator.Validate(command);
        if (validationError is not null)
        {
            return Result.Failure<CreateProductBranchResponse>(validationError);
        }

        var productExists = await _context.Products.AsNoTracking().AnyAsync(p => p.Id == command.ProductId, cancellationToken);
        if (!productExists)
        {
            return Result.Failure<CreateProductBranchResponse>(
                Error.NotFound("ProductBranch.ProductNotFound", $"Product '{command.ProductId}' was not found."));
        }

        var branchExists = await _context.Branches.AsNoTracking().AnyAsync(b => b.Id == command.BranchId, cancellationToken);
        if (!branchExists)
        {
            return Result.Failure<CreateProductBranchResponse>(
                Error.NotFound("ProductBranch.BranchNotFound", $"Branch '{command.BranchId}' was not found."));
        }

        // Pre-check for a friendly 409; the unique index on
        // (ProductId, BranchId) is the real guarantee under concurrency.
        var alreadyExists = await _context.ProductBranches
            .AsNoTracking()
            .AnyAsync(pb => pb.ProductId == command.ProductId && pb.BranchId == command.BranchId, cancellationToken);

        if (alreadyExists)
        {
            return Result.Failure<CreateProductBranchResponse>(
                Error.Conflict("ProductBranch.AlreadyExists", "This product is already onboarded at this branch."));
        }

        var productBranch = new ProductBranch(command.ProductId, command.BranchId, command.SellingPrice);
        productBranch.SetStockThresholds(command.MinimumStock, command.MaximumStock);

        _context.ProductBranches.Add(productBranch);

        await _context.SaveChangesAsync(cancellationToken);
        await _catalogVersionService.IncrementVersionAsync(cancellationToken);

        return Result.Success(new CreateProductBranchResponse(productBranch.Id, productBranch.SellingPrice));
    }
}
