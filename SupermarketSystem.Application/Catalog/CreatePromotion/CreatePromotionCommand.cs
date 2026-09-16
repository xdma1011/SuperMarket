using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Sales;

namespace SupermarketSystem.Application.Catalog.CreatePromotion;

/// <summary>
/// BranchIds فاضية أو null = ينطبق على كل الفروع الفعّالة حاليًا (يُنشأ
/// صف PromotionBranch مفعَّل لكل واحد منها فورًا) - نفس مبدأ CLAUDE.md
/// §1.8 بالعكس تمامًا: هون "بلا تحديد" فعل صريح (كل الفروع الحالية)،
/// لا افتراض ضمني بلا أثر بقاعدة البيانات.
/// </summary>
public sealed record CreatePromotionCommand(
    Guid ProductId,
    string Title,
    int BundleQuantity,
    decimal BundlePrice,
    decimal? MaxQuantityPerInvoice,
    DateTime StartAtUtc,
    DateTime EndAtUtc,
    IReadOnlyList<Guid>? BranchIds);

public sealed record CreatePromotionResponse(Guid PromotionId, string Title, IReadOnlyList<Guid> BranchIds);

public sealed class CreatePromotionHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ICatalogVersionService _catalogVersionService;

    public CreatePromotionHandler(IApplicationDbContext context, ICatalogVersionService catalogVersionService)
    {
        _context = context;
        _catalogVersionService = catalogVersionService;
    }

    public async Task<Result<CreatePromotionResponse>> HandleAsync(CreatePromotionCommand command, CancellationToken cancellationToken)
    {
        var productExists = await _context.Products.AsNoTracking().AnyAsync(p => p.Id == command.ProductId, cancellationToken);
        if (!productExists)
        {
            return Result.Failure<CreatePromotionResponse>(
                Error.NotFound("Promotion.ProductNotFound", $"Product '{command.ProductId}' was not found."));
        }

        IReadOnlyList<Guid> targetBranchIds = command.BranchIds is { Count: > 0 }
            ? command.BranchIds
            : await _context.Branches.AsNoTracking().Where(b => b.IsActive).Select(b => b.Id).ToListAsync(cancellationToken);

        if (targetBranchIds.Count == 0)
        {
            return Result.Failure<CreatePromotionResponse>(
                Error.Validation("Promotion.NoBranches", "No active branches exist to attach this promotion to."));
        }

        var branchesExist = await _context.Branches.AsNoTracking()
            .Where(b => targetBranchIds.Contains(b.Id)).Select(b => b.Id).ToListAsync(cancellationToken);
        var missingBranchId = targetBranchIds.FirstOrDefault(id => !branchesExist.Contains(id));
        if (missingBranchId != Guid.Empty)
        {
            return Result.Failure<CreatePromotionResponse>(
                Error.NotFound("Promotion.BranchNotFound", $"Branch '{missingBranchId}' was not found."));
        }

        Promotion promotion;
        try
        {
            promotion = new Promotion(
                command.ProductId, command.Title, command.BundleQuantity, command.BundlePrice,
                command.MaxQuantityPerInvoice, command.StartAtUtc, command.EndAtUtc);

            foreach (var branchId in targetBranchIds.Distinct())
            {
                promotion.AddBranch(branchId);
            }
        }
        catch (Domain.Common.DomainException ex)
        {
            return Result.Failure<CreatePromotionResponse>(Error.Validation("Promotion.InvalidDetails", ex.Message));
        }

        _context.Promotions.Add(promotion);
        await _context.SaveChangesAsync(cancellationToken);
        await _catalogVersionService.IncrementVersionAsync(cancellationToken);

        return Result.Success(new CreatePromotionResponse(promotion.Id, promotion.Title, targetBranchIds.Distinct().ToList()));
    }
}
