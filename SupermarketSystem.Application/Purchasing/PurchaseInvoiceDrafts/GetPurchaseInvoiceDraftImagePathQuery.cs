using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;

namespace SupermarketSystem.Application.Purchasing.PurchaseInvoiceDrafts;

public sealed record GetPurchaseInvoiceDraftImagePathQuery(Guid DraftId);

/// <summary>
/// يرجّع مسار الملف المحفوظ بس - قراءة الملف نفسه من القرص مسؤولية طبقة
/// API (IO بحت، لا منطق أعمال) لتفادي حمل bytes[] كامل الصورة عبر طبقة
/// Application لغرض إرجاعها فورًا بلا أي معالجة.
/// </summary>
public sealed class GetPurchaseInvoiceDraftImagePathHandler
{
    private readonly IApplicationDbContext _context;

    public GetPurchaseInvoiceDraftImagePathHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<string>> HandleAsync(GetPurchaseInvoiceDraftImagePathQuery query, CancellationToken cancellationToken)
    {
        var imageReference = await _context.PurchaseInvoiceDrafts.AsNoTracking()
            .Where(d => d.Id == query.DraftId)
            .Select(d => d.ImageReference)
            .FirstOrDefaultAsync(cancellationToken);

        if (imageReference is null)
        {
            return Result.Failure<string>(Error.NotFound("PurchaseInvoiceDraft.NotFound", $"مسودة الفاتورة '{query.DraftId}' غير موجودة."));
        }

        return Result.Success(imageReference);
    }
}
