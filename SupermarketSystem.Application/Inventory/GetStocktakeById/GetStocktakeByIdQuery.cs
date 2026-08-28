using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Inventory;

namespace SupermarketSystem.Application.Inventory.GetStocktakeById;

public sealed record GetStocktakeByIdQuery(Guid StocktakeId);

public sealed record StocktakeItemDetailDto(
    Guid StocktakeItemId,
    Guid ProductId,
    string ProductName,
    Guid? ProductBatchId,
    decimal ExpectedQuantity,
    decimal? CountedQuantity,
    decimal? Variance,
    Guid? CountedByUserId,
    DateTime? CountedAtUtc);

public sealed record StocktakeDetailResponse(
    Guid StocktakeId,
    string StocktakeNumber,
    Guid BranchId,
    StocktakeStatus Status,
    DateTime? CompletedAtUtc,
    DateTime? ApprovedAtUtc,
    IReadOnlyList<StocktakeItemDetailDto> Items);

/// <summary>
/// يُستخدم طول دورة حياة الجرد — أثناء العدّ (لمتابعة مين عدّ شو وشو
/// ضل)، وبعد الإكمال (لمراجعة الفروقات قبل الاعتماد)، وبعد الاعتماد
/// (سجل تاريخي كامل لما صار).
/// </summary>
public sealed class GetStocktakeByIdHandler
{
    private readonly IApplicationDbContext _context;

    public GetStocktakeByIdHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<StocktakeDetailResponse>> HandleAsync(GetStocktakeByIdQuery query, CancellationToken cancellationToken)
    {
        var stocktake = await _context.Stocktakes.AsNoTracking()
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.Id == query.StocktakeId, cancellationToken);

        if (stocktake is null)
        {
            return Result.Failure<StocktakeDetailResponse>(
                Error.NotFound("Stocktake.NotFound", $"الجرد '{query.StocktakeId}' غير موجود."));
        }

        var productIds = stocktake.Items.Select(i => i.ProductId).Distinct().ToList();
        var productNames = await _context.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);

        var items = stocktake.Items
            .Select(i => new StocktakeItemDetailDto(
                i.Id, i.ProductId, productNames.GetValueOrDefault(i.ProductId, "(غير معروف)"), i.ProductBatchId,
                i.ExpectedQuantity, i.CountedQuantity, i.VarianceQuantity, i.CountedByUserId, i.CountedAtUtc))
            .ToList();

        return Result.Success(new StocktakeDetailResponse(
            stocktake.Id, stocktake.StocktakeNumber, stocktake.BranchId, stocktake.Status,
            stocktake.CompletedAtUtc, stocktake.ApprovedAtUtc, items));
    }
}
