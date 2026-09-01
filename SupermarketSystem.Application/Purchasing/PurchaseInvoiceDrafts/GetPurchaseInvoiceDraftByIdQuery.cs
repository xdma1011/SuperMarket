using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;

namespace SupermarketSystem.Application.Purchasing.PurchaseInvoiceDrafts;

public sealed record GetPurchaseInvoiceDraftByIdQuery(Guid DraftId);

public sealed record PurchaseInvoiceDraftDetailDto(
    Guid Id,
    Guid BranchId,
    string ImageReference,
    string? ProviderName,
    string? RawSupplierName,
    Guid? MatchedSupplierId,
    string? SupplierInvoiceReference,
    string? InvoiceDate,
    string? Currency,
    decimal? ExtractedInvoiceTotal,
    string? ExtractionConfidence,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<PurchaseInvoiceDraftItemDto> Items,
    int Status,
    Guid? ResultingPurchaseInvoiceId);

public sealed class GetPurchaseInvoiceDraftByIdHandler
{
    private readonly IApplicationDbContext _context;

    public GetPurchaseInvoiceDraftByIdHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<PurchaseInvoiceDraftDetailDto>> HandleAsync(
        GetPurchaseInvoiceDraftByIdQuery query, CancellationToken cancellationToken)
    {
        var draft = await _context.PurchaseInvoiceDrafts.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == query.DraftId, cancellationToken);

        if (draft is null)
        {
            return Result.Failure<PurchaseInvoiceDraftDetailDto>(
                Error.NotFound("PurchaseInvoiceDraft.NotFound", $"مسودة الفاتورة '{query.DraftId}' غير موجودة."));
        }

        return Result.Success(new PurchaseInvoiceDraftDetailDto(
            draft.Id,
            draft.BranchId,
            draft.ImageReference,
            draft.ProviderName,
            draft.RawSupplierName,
            draft.MatchedSupplierId,
            draft.SupplierInvoiceReference,
            draft.InvoiceDate?.ToString("yyyy-MM-dd"),
            draft.Currency,
            draft.ExtractedInvoiceTotal,
            draft.ExtractionConfidence,
            PurchaseInvoiceDraftItemsSerializer.DeserializeWarnings(draft.WarningsText),
            PurchaseInvoiceDraftItemsSerializer.Deserialize(draft.ItemsJson),
            (int)draft.Status,
            draft.ResultingPurchaseInvoiceId));
    }
}
