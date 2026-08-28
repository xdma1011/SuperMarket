using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;

namespace SupermarketSystem.Application.Reporting.GetNegativeStock;

public sealed record GetNegativeStockQuery(PagedRequest Paging, Guid? BranchId);

public sealed record NegativeStockItemDto(
    Guid StockId,
    Guid ProductId,
    string ProductName,
    Guid BranchId,
    Guid? ProductBatchId,
    decimal QuantityOnHand);

/// <summary>
/// تقرير مراجعة إدارية — بيوري كل صف بجدول Stock رصيده تحت الصفر. هذا
/// بيصير فقط لما إعداد Inventory.AllowNegativeStock يكون مفعّل (البيع
/// كمل رغم إن المخزون بالنظام غير كافٍ، لأنه البضاعة فعليًا موجودة/بالطريق).
///
/// نفس فلسفة تقرير الخصومات اليدوية بالظبط: العملية اتسمحت وكملت فورًا
/// بلا ما توقف الكاشير، وهذا التقرير هو "المراجعة بعد الحدث" — يخلي
/// الإدارة تشوف بالضبط أي الأصناف صارت سالبة، بأي فرع، وقديش الكمية،
/// عشان تربطها لاحقًا بفاتورة شراء لسه ما دخلت، أو تتابعها بجرد.
///
/// استعلام قراءة فقط (AsNoTracking)، مفلتر وpaged على مستوى قاعدة
/// البيانات — بلا تحميل الجدول كامل للذاكرة.
/// </summary>
public sealed class GetNegativeStockHandler
{
    private readonly IApplicationDbContext _context;

    public GetNegativeStockHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<NegativeStockItemDto>> HandleAsync(
        GetNegativeStockQuery query,
        CancellationToken cancellationToken)
    {
        var paging = query.Paging.Normalized();

        var negativeStocks = _context.Stocks
            .AsNoTracking()
            .Where(s => s.QuantityOnHand < 0);

        if (query.BranchId is { } branchId)
        {
            negativeStocks = negativeStocks.Where(s => s.BranchId == branchId);
        }

        // الأكتر سالبية أول — الأولوية الطبيعية للمراجعة (أكبر فجوة أولًا).
        negativeStocks = negativeStocks.OrderBy(s => s.QuantityOnHand).ThenBy(s => s.Id);

        var totalCount = await negativeStocks.CountAsync(cancellationToken);

        var items = await negativeStocks
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Join(_context.Products.AsNoTracking(),
                s => s.ProductId,
                p => p.Id,
                (s, p) => new NegativeStockItemDto(
                    s.Id,
                    s.ProductId,
                    p.Name,
                    s.BranchId,
                    s.ProductBatchId,
                    s.QuantityOnHand))
            .ToListAsync(cancellationToken);

        return new PagedResult<NegativeStockItemDto>(items, totalCount, paging.PageNumber, paging.PageSize);
    }
}
