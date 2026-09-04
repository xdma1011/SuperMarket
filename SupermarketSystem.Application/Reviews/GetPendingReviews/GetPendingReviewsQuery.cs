using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;

namespace SupermarketSystem.Application.Reviews.GetPendingReviews;

public enum PendingReviewType
{
    Return = 1,
    ComplimentaryIssue = 2,
    HighPurchasePrice = 3,
    Complaint = 4
}

public sealed record PendingReviewItemDto(
    PendingReviewType Type,
    string TypeTitle,
    Guid ReferenceId,
    string Title,
    string Detail,
    decimal? Amount,
    Guid BranchId,
    DateTime OccurredAtUtc);

public sealed record GetPendingReviewsResponse(IReadOnlyList<PendingReviewItemDto> Items, int TotalCount);

/// <summary>
/// نقطة تجميع واحدة لكل شي "بانتظار مراجعة إدارية" — نفس فلسفة
/// AllowWithReview المطبَّقة بكل النظام. أربعة مصادر حاليًا: ReturnInvoice
/// (كل إرجاع غير مُراجَع بعد)، StockMovement.NeedsReview (ضيافة تجاوزت
/// الحد اليومي)، PurchaseInvoiceItem.NeedsReview (سعر شراء أعلى بنسبة
/// ملحوظة عن متوسط آخر 5 عمليات شراء لنفس المنتج)، وComplaint (شكوى
/// زبون عبر تطبيق الزبائن).
///
/// إضافة مصدر إضافي لاحقًا تعني إضافة استعلام موازٍ هون بس، بلا أي تغيير
/// على شكل الرد أو الفرونت إند.
/// </summary>
public sealed class GetPendingReviewsHandler
{
    private readonly IApplicationDbContext _context;

    public GetPendingReviewsHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<GetPendingReviewsResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var pendingReturns = await _context.ReturnInvoices.AsNoTracking()
            .Where(r => r.ReviewedAtUtc == null)
            .Select(r => new PendingReviewItemDto(
                PendingReviewType.Return,
                "إرجاع",
                r.Id,
                r.InvoiceNumber,
                "إرجاع بانتظار المراجعة",
                r.TotalAmount,
                r.BranchId,
                r.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        var pendingComplimentary = await _context.StockMovements.AsNoTracking()
            .Where(m => m.NeedsReview && m.ReviewedAtUtc == null)
            .Join(_context.Products.AsNoTracking(), m => m.ProductId, p => p.Id, (m, p) => new { Movement = m, ProductName = p.Name })
            .Select(x => new PendingReviewItemDto(
                PendingReviewType.ComplimentaryIssue,
                "ضيافة",
                x.Movement.Id,
                x.ProductName,
                "تجاوزت الحد اليومي المسموح للضيافة",
                x.Movement.QuantityBase,
                x.Movement.BranchId,
                x.Movement.OccurredAtUtc))
            .ToListAsync(cancellationToken);

        // نفس مبدأ CLAUDE.md §3.1: لا تنسيق نص (:F2) جوّا Select() مترجَم
        // لـSQL - نجيب الحقول الخام أول، ونبني نص التفاصيل بالذاكرة.
        var highPriceRows = await _context.PurchaseInvoiceItems.AsNoTracking()
            .Where(i => i.NeedsReview && i.ReviewedAtUtc == null)
            .Join(_context.PurchaseInvoices.AsNoTracking(), i => i.PurchaseInvoiceId, p => p.Id,
                (i, p) => new { i.Id, i.UnitCost, p.InvoiceNumber, p.BranchId, p.CreatedAtUtc, i.ProductId })
            .Join(_context.Products.AsNoTracking(), x => x.ProductId, prod => prod.Id,
                (x, prod) => new { x.Id, x.UnitCost, x.InvoiceNumber, x.BranchId, x.CreatedAtUtc, ProductName = prod.Name })
            .ToListAsync(cancellationToken);

        var pendingHighPrices = highPriceRows
            .Select(x => new PendingReviewItemDto(
                PendingReviewType.HighPurchasePrice,
                "ارتفاع سعر شراء",
                x.Id,
                x.ProductName,
                $"سعر الوحدة {x.UnitCost:F2} بفاتورة {x.InvoiceNumber} - أعلى من المعتاد",
                x.UnitCost,
                x.BranchId,
                x.CreatedAtUtc))
            .ToList();

        // شكوى ممكن ما تكون مرتبطة بطلب (OrderId فاضي) - Order مافيها فرع
        // ثابت بهالحالة، فـBranchId يضل Guid.Empty (مش مستخدَم أصلًا
        // بواجهة صفحة المراجعات الحالية - راجع reviews.component.ts).
        var complaintRows = await _context.Complaints.AsNoTracking()
            .Where(c => !c.IsResolved)
            .Join(_context.Customers.AsNoTracking(), c => c.CustomerId, cust => cust.Id,
                (c, cust) => new { c.Id, c.Text, c.OrderId, CustomerName = cust.FullName, c.CreatedAtUtc })
            .ToListAsync(cancellationToken);

        var orderBranchByOrderId = await _context.Orders.AsNoTracking()
            .Where(o => complaintRows.Select(c => c.OrderId).Contains(o.Id))
            .Select(o => new { o.Id, o.BranchId })
            .ToDictionaryAsync(o => o.Id, o => o.BranchId, cancellationToken);

        var pendingComplaints = complaintRows
            .Select(x => new PendingReviewItemDto(
                PendingReviewType.Complaint,
                "شكوى",
                x.Id,
                x.CustomerName,
                x.Text,
                Amount: null,
                x.OrderId is { } orderId && orderBranchByOrderId.TryGetValue(orderId, out var branchId) ? branchId : Guid.Empty,
                x.CreatedAtUtc))
            .ToList();

        var combined = pendingReturns.Concat(pendingComplimentary).Concat(pendingHighPrices).Concat(pendingComplaints)
            .OrderByDescending(x => x.OccurredAtUtc)
            .ToList();

        return new GetPendingReviewsResponse(combined, combined.Count);
    }
}
