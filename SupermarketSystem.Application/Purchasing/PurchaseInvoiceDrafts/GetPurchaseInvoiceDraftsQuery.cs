using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Domain.Purchasing;

namespace SupermarketSystem.Application.Purchasing.PurchaseInvoiceDrafts;

public sealed record GetPurchaseInvoiceDraftsQuery(PagedRequest Paging, Guid? BranchId, PurchaseInvoiceDraftStatus? Status);

public sealed record PurchaseInvoiceDraftListItemDto(
    Guid Id,
    string? RawSupplierName,
    Guid? MatchedSupplierId,
    string? SupplierInvoiceReference,
    string? ProviderName,
    string? ExtractionConfidence,
    int ItemCount,
    int UnmatchedItemCount,
    int Status,
    DateTime CreatedAtUtc);

public sealed class GetPurchaseInvoiceDraftsHandler
{
    private readonly IApplicationDbContext _context;

    public GetPurchaseInvoiceDraftsHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<PurchaseInvoiceDraftListItemDto>> HandleAsync(
        GetPurchaseInvoiceDraftsQuery query, CancellationToken cancellationToken)
    {
        var paging = query.Paging.Normalized();

        var drafts = _context.PurchaseInvoiceDrafts.AsNoTracking().AsQueryable();

        if (query.BranchId is { } branchId)
        {
            drafts = drafts.Where(d => d.BranchId == branchId);
        }

        drafts = query.Status is { } status
            ? drafts.Where(d => d.Status == status)
            : drafts.Where(d => d.Status == PurchaseInvoiceDraftStatus.PendingReview);

        drafts = drafts.OrderByDescending(d => d.CreatedAtUtc).ThenByDescending(d => d.Id);

        var totalCount = await drafts.CountAsync(cancellationToken);

        var page = await drafts
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Select(d => new
            {
                d.Id,
                d.RawSupplierName,
                d.MatchedSupplierId,
                d.SupplierInvoiceReference,
                d.ProviderName,
                d.ExtractionConfidence,
                d.ItemsJson,
                d.Status,
                d.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        // فك تشفير JSON لا يترجم لـSQL - لازم بالذاكرة، بعد ما جبنا صفحة
        // واحدة بس (Skip/Take قبله)، لا كل السجلات.
        var items = page
            .Select(d =>
            {
                var itemsList = PurchaseInvoiceDraftItemsSerializer.Deserialize(d.ItemsJson);
                return new PurchaseInvoiceDraftListItemDto(
                    d.Id,
                    d.RawSupplierName,
                    d.MatchedSupplierId,
                    d.SupplierInvoiceReference,
                    d.ProviderName,
                    d.ExtractionConfidence,
                    itemsList.Count,
                    itemsList.Count(i => i.MatchedProductId is null),
                    (int)d.Status,
                    d.CreatedAtUtc);
            })
            .ToList();

        return new PagedResult<PurchaseInvoiceDraftListItemDto>(items, totalCount, paging.PageNumber, paging.PageSize);
    }
}
