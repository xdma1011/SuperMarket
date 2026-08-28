using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;

namespace SupermarketSystem.Application.Catalog.GetProductUnits;

public sealed record GetProductUnitsQuery(Guid ProductId);

public sealed record ProductUnitDto(Guid Id, string UnitName, decimal ConversionFactorToBase, bool IsBaseUnit);

public sealed class GetProductUnitsHandler
{
    private readonly IApplicationDbContext _context;

    public GetProductUnitsHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<ProductUnitDto>> HandleAsync(GetProductUnitsQuery query, CancellationToken cancellationToken)
    {
        return await _context.ProductUnits.AsNoTracking()
            .Where(u => u.ProductId == query.ProductId)
            .OrderByDescending(u => u.IsBaseUnit)
            .Select(u => new ProductUnitDto(u.Id, u.UnitName, u.ConversionFactorToBase, u.IsBaseUnit))
            .ToListAsync(cancellationToken);
    }
}
