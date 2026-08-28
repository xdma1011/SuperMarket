using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Common;
using SupermarketSystem.Domain.Inventory;

namespace SupermarketSystem.Application.Inventory.CreateStocktake;

public sealed record CreateStocktakeCommand(
    Guid BranchId,
    bool IncludeAllProductsAtBranch,
    IReadOnlyList<Guid>? ProductIds);

public sealed record CreateStocktakeResponse(Guid StocktakeId, string StocktakeNumber, int ItemCount);

public static class CreateStocktakeValidator
{
    public static Error? Validate(CreateStocktakeCommand command)
    {
        if (command.BranchId == Guid.Empty)
        {
            return Error.Validation("Stocktake.BranchRequired", "فرع مطلوب.");
        }

        if (!command.IncludeAllProductsAtBranch && (command.ProductIds is null || command.ProductIds.Count == 0))
        {
            return Error.Validation(
                "Stocktake.ProductsRequired",
                "إما تحديد قائمة أصناف، أو تفعيل IncludeAllProductsAtBranch لجرد شامل.");
        }

        return null;
    }
}

/// <summary>
/// ExpectedQuantity لكل صنف = رصيد Stock الحالي وقت إنشاء الجرد — لقطة
/// (snapshot)، لا مرجع حي. لو المخزون تغيّر بعد إنشاء الجرد وأثناء العدّ
/// (بيع صار مثلًا)، الفرق هذا بالذات هو جزء طبيعي مما الجرد المفاجئ
/// مصمَّم يكشفه، لا خطأ بالحساب.
///
/// الجرد يُنشأ InProgress مباشرة (Draft يُتخطّى تلقائيًا) — جاهز للعدّ
/// فورًا، بلا خطوة "ابدأ" منفصلة يدوية للحالة الشائعة.
/// </summary>
public sealed class CreateStocktakeHandler
{
    private readonly IApplicationDbContext _context;
    private readonly IDocumentNumberGenerator _documentNumberGenerator;

    public CreateStocktakeHandler(IApplicationDbContext context, IDocumentNumberGenerator documentNumberGenerator)
    {
        _context = context;
        _documentNumberGenerator = documentNumberGenerator;
    }

    public async Task<Result<CreateStocktakeResponse>> HandleAsync(
        CreateStocktakeCommand command, CancellationToken cancellationToken)
    {
        var validationError = CreateStocktakeValidator.Validate(command);
        if (validationError is not null)
        {
            return Result.Failure<CreateStocktakeResponse>(validationError);
        }

        var branchExists = await _context.Branches.AsNoTracking().AnyAsync(b => b.Id == command.BranchId, cancellationToken);
        if (!branchExists)
        {
            return Result.Failure<CreateStocktakeResponse>(
                Error.NotFound("Stocktake.BranchNotFound", $"الفرع '{command.BranchId}' غير موجود."));
        }

        List<Guid> productIds;
        if (command.IncludeAllProductsAtBranch)
        {
            productIds = await _context.ProductBranches.AsNoTracking()
                .Where(pb => pb.BranchId == command.BranchId && pb.IsAvailableForSale)
                .Select(pb => pb.ProductId)
                .ToListAsync(cancellationToken);
        }
        else
        {
            productIds = command.ProductIds!.Distinct().ToList();

            var existingCount = await _context.Products.AsNoTracking()
                .CountAsync(p => productIds.Contains(p.Id), cancellationToken);
            if (existingCount != productIds.Count)
            {
                return Result.Failure<CreateStocktakeResponse>(
                    Error.Validation("Stocktake.ProductNotFound", "قائمة الأصناف تحتوي معرّف منتج غير موجود."));
            }
        }

        if (productIds.Count == 0)
        {
            return Result.Failure<CreateStocktakeResponse>(
                Error.Validation("Stocktake.NoProductsToCount", "لا يوجد أي صنف مؤهَّل للجرد بهذا النطاق."));
        }

        var currentStocks = await _context.Stocks.AsNoTracking()
            .Where(s => s.BranchId == command.BranchId && productIds.Contains(s.ProductId))
            .ToListAsync(cancellationToken);

        var stockByProduct = currentStocks
            .GroupBy(s => s.ProductId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var stocktakeNumber = await _documentNumberGenerator.GetNextNumberAsync(
            command.BranchId, DocumentType.Stocktake, cancellationToken);

        var stocktake = new Stocktake(command.BranchId, stocktakeNumber);

        foreach (var productId in productIds)
        {
            if (stockByProduct.TryGetValue(productId, out var stockRows) && stockRows.Count > 0)
            {
                foreach (var stockRow in stockRows)
                {
                    stocktake.AddItem(productId, stockRow.ProductBatchId, stockRow.QuantityOnHand);
                }
            }
            else
            {
                stocktake.AddItem(productId, productBatchId: null, expectedQuantity: 0m);
            }
        }

        stocktake.Begin();

        _context.Stocktakes.Add(stocktake);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(new CreateStocktakeResponse(stocktake.Id, stocktake.StocktakeNumber, stocktake.Items.Count));
    }
}
