using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;

namespace SupermarketSystem.Application.Catalog.GetProductPromotions;

public sealed record GetProductPromotionsQuery(Guid ProductId);

public sealed record PromotionBranchDto(Guid PromotionBranchId, Guid BranchId, string BranchName, bool IsActive);

public sealed record ProductPromotionDto(
    Guid PromotionId,
    string Title,
    int BundleQuantity,
    decimal BundlePrice,
    decimal? MaxQuantityPerInvoice,
    DateTime StartAtUtc,
    DateTime EndAtUtc,
    IReadOnlyList<PromotionBranchDto> Branches);

/// <summary>يعرض عروض منتج بلا تحقق تاريخ/تفعيل - إدارة كاملة (ماضي، حالي، مستقبلي) لا "الشغّال الآن" بس (ذاك دور GetPublicCatalog/GetCatalogSyncPage).</summary>
public sealed class GetProductPromotionsHandler
{
    private readonly IApplicationDbContext _context;

    public GetProductPromotionsHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<ProductPromotionDto>> HandleAsync(GetProductPromotionsQuery query, CancellationToken cancellationToken)
    {
        var promotions = await _context.Promotions.AsNoTracking()
            .Where(p => p.ProductId == query.ProductId)
            .OrderByDescending(p => p.CreatedAtUtc)
            .Select(p => new { p.Id, p.Title, p.BundleQuantity, p.BundlePrice, p.MaxQuantityPerInvoice, p.StartAtUtc, p.EndAtUtc })
            .ToListAsync(cancellationToken);

        var promotionIds = promotions.Select(p => p.Id).ToList();

        var branchLinks = await _context.PromotionBranches.AsNoTracking()
            .Where(pb => promotionIds.Contains(pb.PromotionId))
            .Join(_context.Branches.AsNoTracking(), pb => pb.BranchId, b => b.Id,
                (pb, b) => new { pb.PromotionId, pb.Id, pb.BranchId, BranchName = b.Name, pb.IsActive })
            .ToListAsync(cancellationToken);

        return promotions.Select(p => new ProductPromotionDto(
            p.Id, p.Title, p.BundleQuantity, p.BundlePrice, p.MaxQuantityPerInvoice, p.StartAtUtc, p.EndAtUtc,
            branchLinks.Where(b => b.PromotionId == p.Id)
                .Select(b => new PromotionBranchDto(b.Id, b.BranchId, b.BranchName, b.IsActive))
                .ToList()))
            .ToList();
    }
}
