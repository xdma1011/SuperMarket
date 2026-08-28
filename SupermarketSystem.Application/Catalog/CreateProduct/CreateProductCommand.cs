using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Catalog;

namespace SupermarketSystem.Application.Catalog.CreateProduct;

public sealed record CreateProductUnitDto(string UnitName, decimal ConversionFactorToBase, bool IsBaseUnit);

/// <summary>UnitName must match the UnitName of one entry in the command's Units list — units have no id yet at request time.</summary>
public sealed record CreateProductBarcodeDto(string BarcodeValue, string UnitName);

public sealed record CreateProductCommand(
    string Name,
    string? Description,
    Guid CategoryId,
    bool IsBatchTracked,
    decimal? SuggestedRetailPrice,
    int? ExpectedShelfLifeDays,
    IReadOnlyList<CreateProductUnitDto> Units,
    IReadOnlyList<CreateProductBarcodeDto> Barcodes);

public sealed record CreateProductResponse(Guid ProductId, string Name);

public static class CreateProductValidator
{
    private const int MaxNameLength = 300;
    private const int MaxDescriptionLength = 2000;

    public static Error? Validate(CreateProductCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
        {
            return Error.Validation("Product.NameRequired", "Product name is required.");
        }

        if (command.Name.Length > MaxNameLength)
        {
            return Error.Validation("Product.NameTooLong", $"Product name cannot exceed {MaxNameLength} characters.");
        }

        if (command.Description is { Length: > MaxDescriptionLength })
        {
            return Error.Validation("Product.DescriptionTooLong", $"Description cannot exceed {MaxDescriptionLength} characters.");
        }

        if (command.CategoryId == Guid.Empty)
        {
            return Error.Validation("Product.CategoryRequired", "A category is required.");
        }

        if (command.SuggestedRetailPrice is < 0)
        {
            return Error.Validation("Product.SuggestedRetailPriceNegative", "Suggested retail price cannot be negative.");
        }

        if (command.Units.Count == 0)
        {
            return Error.Validation("Product.UnitsRequired", "At least one unit is required.");
        }

        var baseUnitCount = command.Units.Count(u => u.IsBaseUnit);
        if (baseUnitCount != 1)
        {
            return Error.Validation("Product.ExactlyOneBaseUnit", "Exactly one unit must be marked as the base unit.");
        }

        if (command.Units.Any(u => u.ConversionFactorToBase <= 0))
        {
            return Error.Validation("Product.InvalidConversionFactor", "Every unit's conversion factor must be positive.");
        }

        var baseUnit = command.Units.First(u => u.IsBaseUnit);
        if (baseUnit.ConversionFactorToBase != 1m)
        {
            return Error.Validation("Product.BaseUnitFactorMustBeOne", "The base unit's conversion factor must be exactly 1.");
        }

        if (command.Units.Select(u => u.UnitName.Trim().ToUpperInvariant()).Distinct().Count() != command.Units.Count)
        {
            return Error.Validation("Product.DuplicateUnitName", "Unit names must be unique within a product.");
        }

        var unitNames = command.Units.Select(u => u.UnitName.Trim().ToUpperInvariant()).ToHashSet();
        foreach (var barcode in command.Barcodes)
        {
            if (string.IsNullOrWhiteSpace(barcode.BarcodeValue))
            {
                return Error.Validation("Product.BarcodeValueRequired", "Barcode value cannot be empty.");
            }

            if (!unitNames.Contains(barcode.UnitName.Trim().ToUpperInvariant()))
            {
                return Error.Validation(
                    "Product.BarcodeUnitNotFound",
                    $"Barcode '{barcode.BarcodeValue}' references unit '{barcode.UnitName}', which is not in the Units list.");
            }
        }

        if (command.Barcodes.Select(b => b.BarcodeValue.Trim()).Distinct().Count() != command.Barcodes.Count)
        {
            return Error.Validation("Product.DuplicateBarcodeInRequest", "Barcode values must be unique within the request.");
        }

        return null;
    }
}

/// <summary>
/// Creates a Product together with its Units and Barcodes as one aggregate,
/// one transaction. Barcodes are global-unique (Architecture Review §13
/// indexing) and cannot exist without a unit, so this handler resolves each
/// barcode's UnitName to the actual ProductUnit created moments earlier in
/// the same call — units have no id until Product.AddUnit assigns one, which
/// is why barcodes reference units by name in the request DTO rather than by
/// a not-yet-existing id.
/// </summary>
public sealed class CreateProductHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ICatalogVersionService _catalogVersionService;

    public CreateProductHandler(IApplicationDbContext context, ICatalogVersionService catalogVersionService)
    {
        _context = context;
        _catalogVersionService = catalogVersionService;
    }

    public async Task<Result<CreateProductResponse>> HandleAsync(
        CreateProductCommand command,
        CancellationToken cancellationToken)
    {
        var validationError = CreateProductValidator.Validate(command);
        if (validationError is not null)
        {
            return Result.Failure<CreateProductResponse>(validationError);
        }

        var categoryExists = await _context.ProductCategories
            .AsNoTracking()
            .AnyAsync(c => c.Id == command.CategoryId, cancellationToken);

        if (!categoryExists)
        {
            return Result.Failure<CreateProductResponse>(
                Error.NotFound("Product.CategoryNotFound", $"Category '{command.CategoryId}' was not found."));
        }

        var requestedBarcodeValues = command.Barcodes.Select(b => b.BarcodeValue.Trim()).ToList();
        if (requestedBarcodeValues.Count > 0)
        {
            // Global uniqueness pre-check. The unique index on
            // ProductBarcode.BarcodeValue is the real guarantee; this exists
            // to fail with a clear 409 instead of surfacing a raw SQL error.
            var conflictingBarcode = await _context.ProductBarcodes
                .AsNoTracking()
                .Where(b => requestedBarcodeValues.Contains(b.BarcodeValue))
                .Select(b => b.BarcodeValue)
                .FirstOrDefaultAsync(cancellationToken);

            if (conflictingBarcode is not null)
            {
                return Result.Failure<CreateProductResponse>(
                    Error.Conflict("Product.BarcodeAlreadyExists", $"Barcode '{conflictingBarcode}' is already assigned to another product."));
            }
        }

        var product = new Product(command.Name.Trim(), command.CategoryId, command.IsBatchTracked, command.SuggestedRetailPrice, command.ExpectedShelfLifeDays);
        product.SetDescription(command.Description?.Trim());

        var unitsByName = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var unitDto in command.Units)
        {
            var unit = product.AddUnit(unitDto.UnitName.Trim(), unitDto.ConversionFactorToBase, unitDto.IsBaseUnit);
            unitsByName[unitDto.UnitName.Trim()] = unit.Id;
        }

        foreach (var barcodeDto in command.Barcodes)
        {
            var unitId = unitsByName[barcodeDto.UnitName.Trim()];
            product.AddBarcode(barcodeDto.BarcodeValue.Trim(), unitId);
        }

        _context.Products.Add(product);

        await _context.SaveChangesAsync(cancellationToken);
        await _catalogVersionService.IncrementVersionAsync(cancellationToken);

        return Result.Success(new CreateProductResponse(product.Id, product.Name));
    }
}
