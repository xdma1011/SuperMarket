using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Domain.Sales;

namespace SupermarketSystem.Application.Reporting.GetBestCashiers;

public sealed record GetBestCashiersQuery(PagedRequest Paging, Guid? BranchId, DateTime FromUtc, DateTime ToUtc);

public sealed record BestCashierItemDto(
    // null يعني فواتير اتسجلت بلا مستخدم حقيقي (قبل تفعيل المصادقة —
    // راجع User.SystemUserId). نعرضها كبند منفصل بدل ما نخفيها أو نرميها
    // خطأ، بلا ما ندّعي فيها انتساب لشخص معيّن.
    Guid? CashierUserId,
    string CashierUsername,
    int InvoiceCount,
    decimal TotalSales);

/// <summary>
/// أفضل كاشير حسب إجمالي المبيعات (بلا الفواتير الملغاة) خلال فترة محددة.
/// "الأفضل" هون بمعنى الرقم فقط — بلا أي حكم أداء ضمني (كاشير مبيعاته أعلى
/// مش بالضرورة أفضل أداء، ممكن يكون أطول دوام مثلًا). الترتيب والتفسير
/// يرجع للإدارة.
/// </summary>
public sealed class GetBestCashiersHandler
{
    private readonly IApplicationDbContext _context;

    public GetBestCashiersHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<BestCashierItemDto>> HandleAsync(GetBestCashiersQuery query, CancellationToken cancellationToken)
    {
        var paging = query.Paging.Normalized();

        var invoices = _context.SaleInvoices.AsNoTracking()
            .Where(s => s.Status != SaleInvoiceStatus.Voided
                        && s.CreatedAtUtc >= query.FromUtc && s.CreatedAtUtc <= query.ToUtc);

        if (query.BranchId is { } branchId)
        {
            invoices = invoices.Where(s => s.BranchId == branchId);
        }

        var grouped = invoices
            .GroupBy(s => s.CreatedByUserId)
            .Select(g => new
            {
                CashierUserId = g.Key,
                InvoiceCount = g.Count(),
                TotalSales = g.Sum(s => s.TotalAmount)
            });

        var totalCount = await grouped.CountAsync(cancellationToken);

        // Left join يدوي (GroupJoin + SelectMany) — نفس نمط تقرير المرتجعات
        // الحديثة: صف بلا مستخدم مُحلّل ما لازم يختفي من التقرير.
        var page = await grouped
            .OrderByDescending(g => g.TotalSales)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .GroupJoin(_context.Users.AsNoTracking(),
                g => g.CashierUserId,
                u => (Guid?)u.Id,
                (g, matchedUsers) => new { g, matchedUsers })
            .SelectMany(
                x => x.matchedUsers.DefaultIfEmpty(),
                (x, u) => new BestCashierItemDto(
                    x.g.CashierUserId,
                    u != null ? u.Username : "(غير معروف)",
                    x.g.InvoiceCount,
                    x.g.TotalSales))
            .ToListAsync(cancellationToken);

        return new PagedResult<BestCashierItemDto>(page, totalCount, paging.PageNumber, paging.PageSize);
    }
}
