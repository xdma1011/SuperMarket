using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;

namespace SupermarketSystem.Application.Catalog.GetUnitsOfMeasure;

public sealed record GetUnitsOfMeasureQuery(bool ActiveOnly);

public sealed record UnitOfMeasureDto(Guid Id, string Name, bool IsActive);

/// <summary>
/// كانت مفقودة بالكامل - وحدة كل منتج (ProductUnit.UnitName) كانت نص حر
/// يُكتب من جديد بكل مرة، بلا مرجع موحَّد يمنع تكرار مسميات مختلفة
/// لنفس الوحدة (مثلًا "كيلو" مرة و"كغم" مرة تانية). قائمة صغيرة عمدًا
/// (بلا Pagination) - عدد وحدات القياس بمحل واحد محدود بطبيعته.
/// </summary>
public sealed class GetUnitsOfMeasureHandler
{
    private readonly IApplicationDbContext _context;

    public GetUnitsOfMeasureHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<UnitOfMeasureDto>> HandleAsync(GetUnitsOfMeasureQuery query, CancellationToken cancellationToken)
    {
        var units = _context.UnitsOfMeasure.AsNoTracking().AsQueryable();

        if (query.ActiveOnly)
        {
            units = units.Where(u => u.IsActive);
        }

        return await units
            .OrderBy(u => u.Name)
            .Select(u => new UnitOfMeasureDto(u.Id, u.Name, u.IsActive))
            .ToListAsync(cancellationToken);
    }
}
