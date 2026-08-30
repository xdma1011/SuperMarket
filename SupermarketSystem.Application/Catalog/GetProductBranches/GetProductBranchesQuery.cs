using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;

namespace SupermarketSystem.Application.Catalog.GetProductBranches;

public sealed record GetProductBranchesQuery(Guid ProductId);

public sealed record ProductBranchItemDto(
    Guid ProductBranchId, Guid BranchId, string BranchName,
    decimal SellingPrice, decimal? MinimumStock, decimal? MaximumStock, bool IsAvailableForSale);

/// <summary>
/// كانت ناقصة بالكامل — endpoint إضافة منتج لفرع موجود من جلسة قديمة،
/// بس صفر طريقة تشوف "أي فروع مربوط فيها هالمنتج حاليًا". فجوة حقيقية
/// خلّت منتجات جديدة "غير مرئية" للكاشير (المزامنة تبدأ من
/// ProductBranches تحديدًا).
/// </summary>
public sealed class GetProductBranchesHandler
{
    private readonly IApplicationDbContext _context;

    public GetProductBranchesHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<ProductBranchItemDto>> HandleAsync(GetProductBranchesQuery query, CancellationToken cancellationToken)
    {
        return await _context.ProductBranches.AsNoTracking()
            .Where(pb => pb.ProductId == query.ProductId)
            .Join(_context.Branches.AsNoTracking(), pb => pb.BranchId, b => b.Id,
                (pb, b) => new ProductBranchItemDto(
                    pb.Id, pb.BranchId, b.Name, pb.SellingPrice, pb.MinimumStock, pb.MaximumStock, pb.IsAvailableForSale))
            .ToListAsync(cancellationToken);
    }
}
