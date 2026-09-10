using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;

namespace SupermarketSystem.Application.Catalog.SetUnitOfMeasureActive;

public sealed record SetUnitOfMeasureActiveCommand(Guid UnitOfMeasureId, bool IsActive);

/// <summary>
/// إيقاف لا حذف فعلي عمدًا - منتجات موجودة ممكن يكون عندها ProductUnit.UnitName
/// يطابق اسم وحدة صارت موقَفة، حذفها الفعلي كان رح يكسر المرجع بلا داعي؛
/// الإيقاف بس يمنعها من الظهور بقائمة الاختيار لمنتج جديد.
/// </summary>
public sealed class SetUnitOfMeasureActiveHandler
{
    private readonly IApplicationDbContext _context;

    public SetUnitOfMeasureActiveHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result> HandleAsync(SetUnitOfMeasureActiveCommand command, CancellationToken cancellationToken)
    {
        var unit = await _context.UnitsOfMeasure.FirstOrDefaultAsync(u => u.Id == command.UnitOfMeasureId, cancellationToken);
        if (unit is null)
        {
            return Result.Failure(Error.NotFound("UnitOfMeasure.NotFound", $"وحدة القياس '{command.UnitOfMeasureId}' غير موجودة."));
        }

        if (command.IsActive)
        {
            unit.Activate();
        }
        else
        {
            unit.Deactivate();
        }

        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
