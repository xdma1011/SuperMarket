using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Catalog;

namespace SupermarketSystem.Application.Catalog.CreateUnitOfMeasure;

public sealed record CreateUnitOfMeasureCommand(string Name);

public sealed record CreateUnitOfMeasureResponse(Guid UnitOfMeasureId, string Name);

public sealed class CreateUnitOfMeasureHandler
{
    private readonly IApplicationDbContext _context;

    public CreateUnitOfMeasureHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<CreateUnitOfMeasureResponse>> HandleAsync(CreateUnitOfMeasureCommand command, CancellationToken cancellationToken)
    {
        var name = command.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<CreateUnitOfMeasureResponse>(
                Error.Validation("UnitOfMeasure.NameRequired", "اسم وحدة القياس مطلوب."));
        }

        // فحص أوّلي ودّي - الفهرس الفريد بقاعدة البيانات هو الحارس الفعلي تحت التزامن.
        var exists = await _context.UnitsOfMeasure.AsNoTracking().AnyAsync(u => u.Name == name, cancellationToken);
        if (exists)
        {
            return Result.Failure<CreateUnitOfMeasureResponse>(
                Error.Conflict("UnitOfMeasure.AlreadyExists", $"وحدة قياس بالاسم '{name}' موجودة أصلًا."));
        }

        var unit = new UnitOfMeasure(name);
        _context.UnitsOfMeasure.Add(unit);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(new CreateUnitOfMeasureResponse(unit.Id, unit.Name));
    }
}
