using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;

namespace SupermarketSystem.Application.Reviews.GetPendingReviews;

public enum PendingReviewType
{
    Return = 1,
    ComplimentaryIssue = 2
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
/// AllowWithReview المطبَّقة بكل النظام. حاليًا مصدران: ReturnInvoice
/// (كل إرجاع غير مُراجَع بعد) وStockMovement.NeedsReview (ضيافة تجاوزت
/// الحد اليومي حاليًا، وأي نوع حركة لاحق بلا تغيير هيكلي).
///
/// إضافة مصدر ثالث لاحقًا تعني إضافة استعلام موازٍ هون بس، بلا أي تغيير
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

        var combined = pendingReturns.Concat(pendingComplimentary)
            .OrderByDescending(x => x.OccurredAtUtc)
            .ToList();

        return new GetPendingReviewsResponse(combined, combined.Count);
    }
}
