using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;

namespace SupermarketSystem.Application.Catalog.MoveUnitOfMeasure;

public sealed record MoveUnitOfMeasureCommand(Guid UnitOfMeasureId, bool MoveUp);

/// <summary>
/// تحريك وحدة خانة لفوق أو لتحت بقائمة الترتيب (أسهل طريقة ممكنة من
/// الواجهة: سهمين). بيعيد ترقيم كل الوحدات 1..n بالترتيب الحالي ثم
/// بيبدّل الوحدة مع جارتها - فأي أرقام مكرَّرة أو فجوات قديمة بتنصلح
/// تلقائيًا مع أول تحريك. الوحدة الأولى لفوق/الأخيرة لتحت = بلا تغيير.
/// </summary>
public sealed class MoveUnitOfMeasureHandler
{
    private readonly IApplicationDbContext _context;

    public MoveUnitOfMeasureHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result> HandleAsync(MoveUnitOfMeasureCommand command, CancellationToken cancellationToken)
    {
        var units = await _context.UnitsOfMeasure
            .OrderBy(u => u.SortOrder)
            .ThenBy(u => u.Name)
            .ToListAsync(cancellationToken);

        var index = units.FindIndex(u => u.Id == command.UnitOfMeasureId);
        if (index < 0)
        {
            return Result.Failure(Error.NotFound("UnitOfMeasure.NotFound", $"وحدة القياس '{command.UnitOfMeasureId}' غير موجودة."));
        }

        var targetIndex = command.MoveUp ? index - 1 : index + 1;
        if (targetIndex >= 0 && targetIndex < units.Count)
        {
            (units[index], units[targetIndex]) = (units[targetIndex], units[index]);
        }

        for (var i = 0; i < units.Count; i++)
        {
            if (units[i].SortOrder != i + 1)
            {
                units[i].SetSortOrder(i + 1);
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
