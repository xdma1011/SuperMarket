using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Identity;
using SupermarketSystem.Domain.Inventory;

namespace SupermarketSystem.Application.Inventory.RecordStocktakeCount;

public sealed record RecordStocktakeCountCommand(Guid StocktakeId, Guid StocktakeItemId, decimal CountedQuantity);

public sealed record RecordStocktakeCountResponse(Guid StocktakeItemId, decimal ExpectedQuantity, decimal CountedQuantity, decimal Variance);

public static class RecordStocktakeCountValidator
{
    public static Error? Validate(RecordStocktakeCountCommand command)
    {
        if (command.CountedQuantity < 0)
        {
            return Error.Validation("Stocktake.CountedQuantityNegative", "الكمية المعدودة لا يمكن أن تكون سالبة.");
        }

        return null;
    }
}

/// <summary>
/// كل مستخدم عنده صلاحية يقدر يسجّل عدّ لأي صنف بالجرد، بشكل مستقل عن
/// باقي المستخدمين — هذا بالضبط ما يدعم "الجرد متعدد المستخدمين": فريق
/// كامل يعدّ أقسام مختلفة من نفس الجرد بالتوازي، كل واحد بيلمس أسطره هو
/// بس. آخر عدّ لنفس السطر هو المعتمَد (last write wins) — تعارض حقيقي
/// على *نفس* الصنف بالضبط بنفس اللحظة حالة نادرة جدًا عمليًا.
/// </summary>
public sealed class RecordStocktakeCountHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;

    public RecordStocktakeCountHandler(
        IApplicationDbContext context, ICurrentUserContext currentUser, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<RecordStocktakeCountResponse>> HandleAsync(
        RecordStocktakeCountCommand command, CancellationToken cancellationToken)
    {
        var validationError = RecordStocktakeCountValidator.Validate(command);
        if (validationError is not null)
        {
            return Result.Failure<RecordStocktakeCountResponse>(validationError);
        }

        var stocktake = await _context.Stocktakes
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.Id == command.StocktakeId, cancellationToken);

        if (stocktake is null)
        {
            return Result.Failure<RecordStocktakeCountResponse>(
                Error.NotFound("Stocktake.NotFound", $"الجرد '{command.StocktakeId}' غير موجود."));
        }

        if (stocktake.Status != StocktakeStatus.InProgress)
        {
            return Result.Failure<RecordStocktakeCountResponse>(
                Error.BusinessRule("Stocktake.NotInProgress", $"لا يمكن تسجيل عدّ على جرد بحالة {stocktake.Status}."));
        }

        var item = stocktake.Items.FirstOrDefault(i => i.Id == command.StocktakeItemId);
        if (item is null)
        {
            return Result.Failure<RecordStocktakeCountResponse>(
                Error.NotFound("Stocktake.ItemNotFound", $"سطر الجرد '{command.StocktakeItemId}' غير موجود بهذا الجرد."));
        }

        var actorUserId = _currentUser.UserId ?? User.SystemUserId;
        item.RecordCount(command.CountedQuantity, actorUserId, _dateTimeProvider.UtcNow);

        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(new RecordStocktakeCountResponse(
            item.Id, item.ExpectedQuantity, item.CountedQuantity!.Value, item.VarianceQuantity!.Value));
    }
}
