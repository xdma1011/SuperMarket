using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;

namespace SupermarketSystem.Application.Reporting.GetManualDiscounts;

/// <summary>
/// Level distinguishes a per-line manual discount from a whole-order one;
/// ProductId is null for Invoice-level rows. CashierUserId is nullable for
/// the same reason as GetRecentReturns.RecentReturnItemDto — CreatedByUserId
/// is currently always null under PlaceholderCurrentUserContext, and a
/// discount record must remain visible in this report regardless.
/// </summary>
public sealed record ManualDiscountItemDto(
    string Level,
    Guid SaleInvoiceId,
    string InvoiceNumber,
    Guid BranchId,
    Guid? ProductId,
    decimal DiscountAmount,
    Guid? CashierUserId,
    DateTime CreatedAtUtc);

public sealed record GetManualDiscountsQuery(PagedRequest Paging, Guid? BranchId, DateTime? FromUtc, DateTime? ToUtc);

/// <summary>
/// Architecture Review §16.10/§13.6: a manual discount IS a discount snapshot
/// with DiscountId == NULL — there is no separate stored flag, that absence
/// is the signal. This query is exactly what makes that absence queryable at
/// both the line level (a single item discounted at checkout) and the
/// invoice level (a whole-order discount), unioned into one management view.
/// </summary>
public sealed class GetManualDiscountsHandler
{
    private readonly IApplicationDbContext _context;

    public GetManualDiscountsHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<ManualDiscountItemDto>> HandleAsync(
        GetManualDiscountsQuery query,
        CancellationToken cancellationToken)
    {
        var paging = query.Paging.Normalized();

        var invoices = _context.SaleInvoices.AsNoTracking().AsQueryable();

        if (query.BranchId is { } branchId)
        {
            invoices = invoices.Where(s => s.BranchId == branchId);
        }

        if (query.FromUtc is { } fromUtc)
        {
            invoices = invoices.Where(s => s.CreatedAtUtc >= fromUtc);
        }

        if (query.ToUtc is { } toUtc)
        {
            invoices = invoices.Where(s => s.CreatedAtUtc <= toUtc);
        }

        // خطأ حقيقي كان موجودًا: التعليق الأصلي هون كان يدّعي "EF Core
        // يترجم Concat لـUNION ALL"، بس هذا ما بيصير فعليًا لما كل فرع من
        // الـConcat يكون معمول له .Select(new ManualDiscountItemDto(...))
        // (إسقاط لـrecord) *قبل* الدمج - EF بيرمي "Unable to translate set
        // operation after client projection has been applied" بكل استدعاء
        // فعلي، بلا استثناء (كان هذا التقرير معطَّل بالكامل بالإنتاج).
        // الحل: إسقاط لنوع anonymous موحَّد بالطرفين أول (Concat بينهم
        // بيترجم UNION ALL بأمان)، وبس بعدين إسقاط واحد نهائي لـManualDiscountItemDto.
        var lineLevel = _context.SaleInvoiceItems
            .AsNoTracking()
            .Where(i => i.DiscountId == null && i.DiscountSnapshot > 0)
            .Join(invoices, i => i.SaleInvoiceId, s => s.Id, (i, s) => new
            {
                Level = "Line",
                SaleInvoiceId = s.Id,
                s.InvoiceNumber,
                s.BranchId,
                ProductId = (Guid?)i.ProductId,
                DiscountAmount = i.DiscountSnapshot,
                s.CreatedByUserId,
                s.CreatedAtUtc
            });

        var invoiceLevel = invoices
            .Where(s => s.DiscountId == null && s.DiscountAmountSnapshot > 0)
            .Select(s => new
            {
                Level = "Invoice",
                SaleInvoiceId = s.Id,
                s.InvoiceNumber,
                s.BranchId,
                ProductId = (Guid?)null,
                DiscountAmount = s.DiscountAmountSnapshot,
                s.CreatedByUserId,
                s.CreatedAtUtc
            });

        var combined = lineLevel.Concat(invoiceLevel);

        var totalCount = await combined.CountAsync(cancellationToken);

        var items = await combined
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Select(x => new ManualDiscountItemDto(
                x.Level, x.SaleInvoiceId, x.InvoiceNumber, x.BranchId,
                x.ProductId, x.DiscountAmount, x.CreatedByUserId, x.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return new PagedResult<ManualDiscountItemDto>(items, totalCount, paging.PageNumber, paging.PageSize);
    }
}
