using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;

namespace SupermarketSystem.Application.Catalog.GetProductUnits;

public sealed record GetProductUnitsQuery(Guid ProductId);

public sealed record ProductBarcodeDto(Guid Id, string Value);

/// <summary>BarcodeValue = الباركود الأساسي (الأول)؛ ExtraBarcodes = الباركودات الإضافية للوحدة (نسخ بلدان/موزّعين مختلفين).</summary>
public sealed record ProductUnitDto(
    Guid Id, string UnitName, decimal ConversionFactorToBase, bool IsBaseUnit, string? BarcodeValue,
    IReadOnlyList<ProductBarcodeDto>? ExtraBarcodes = null);

public sealed class GetProductUnitsHandler
{
    private readonly IApplicationDbContext _context;

    public GetProductUnitsHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<ProductUnitDto>> HandleAsync(GetProductUnitsQuery query, CancellationToken cancellationToken)
    {
        var units = await _context.ProductUnits.AsNoTracking()
            .Where(u => u.ProductId == query.ProductId)
            .OrderByDescending(u => u.IsBaseUnit)
            .Select(u => new { u.Id, u.UnitName, u.ConversionFactorToBase, u.IsBaseUnit })
            .ToListAsync(cancellationToken);

        var barcodes = await _context.ProductBarcodes.AsNoTracking()
            .Where(b => b.ProductId == query.ProductId)
            .OrderBy(b => b.CreatedAtUtc).ThenBy(b => b.Id)
            .Select(b => new { b.Id, b.ProductUnitId, b.BarcodeValue })
            .ToListAsync(cancellationToken);

        return units.Select(u =>
        {
            var own = barcodes.Where(b => b.ProductUnitId == u.Id).ToList();
            return new ProductUnitDto(
                u.Id, u.UnitName, u.ConversionFactorToBase, u.IsBaseUnit,
                own.FirstOrDefault()?.BarcodeValue,
                own.Skip(1).Select(b => new ProductBarcodeDto(b.Id, b.BarcodeValue)).ToList());
        }).ToList();
    }
}
