using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;

namespace SupermarketSystem.Application.Inventory.CompleteStocktake;

public sealed record CompleteStocktakeCommand(Guid StocktakeId);

public sealed record CompleteStocktakeVarianceDto(Guid StocktakeItemId, Guid ProductId, decimal ExpectedQuantity, decimal CountedQuantity, decimal Variance);

public sealed record CompleteStocktakeResponse(
    Guid StocktakeId,
    string StocktakeNumber,
    IReadOnlyList<CompleteStocktakeVarianceDto> Variances);

/// <summary>
/// يقفل مرحلة العدّ فقط — لا يلمس Stock إطلاقًا. الرد بيرجّع كل الفروقات
/// (اللي مو صفر) عشان تُراجَع، بالضبط حسب المتطلب: "الفروقات تظهر قبل
/// الاعتماد النهائي". التطبيق الفعلي على المخزون يصير بخطوة منفصلة
/// (ApproveStocktakeHandler)، مش هون.
/// </summary>
public sealed class CompleteStocktakeHandler
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _dateTimeProvider;

    public CompleteStocktakeHandler(IApplicationDbContext context, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<CompleteStocktakeResponse>> HandleAsync(
        CompleteStocktakeCommand command, CancellationToken cancellationToken)
    {
        var stocktake = await _context.Stocktakes
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.Id == command.StocktakeId, cancellationToken);

        if (stocktake is null)
        {
            return Result.Failure<CompleteStocktakeResponse>(
                Error.NotFound("Stocktake.NotFound", $"الجرد '{command.StocktakeId}' غير موجود."));
        }

        try
        {
            stocktake.Complete(_dateTimeProvider.UtcNow);
        }
        catch (SupermarketSystem.Domain.Common.DomainException ex)
        {
            // بما فيها "في أصناف ما اتعدّت بعد" — Domain.Complete() نفسها
            // بترفض، هون بس نترجمها لخطأ Result بدل استثناء يوصل للمستدعي خام.
            return Result.Failure<CompleteStocktakeResponse>(Error.BusinessRule("Stocktake.CannotComplete", ex.Message));
        }

        await _context.SaveChangesAsync(cancellationToken);

        var variances = stocktake.Items
            .Where(i => i.VarianceQuantity != 0)
            .Select(i => new CompleteStocktakeVarianceDto(i.Id, i.ProductId, i.ExpectedQuantity, i.CountedQuantity!.Value, i.VarianceQuantity!.Value))
            .ToList();

        return Result.Success(new CompleteStocktakeResponse(stocktake.Id, stocktake.StocktakeNumber, variances));
    }
}
