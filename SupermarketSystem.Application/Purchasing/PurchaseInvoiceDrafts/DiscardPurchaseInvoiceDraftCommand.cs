using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Purchasing;

namespace SupermarketSystem.Application.Purchasing.PurchaseInvoiceDrafts;

public sealed record DiscardPurchaseInvoiceDraftCommand(Guid DraftId);

public sealed class DiscardPurchaseInvoiceDraftHandler
{
    private readonly IApplicationDbContext _context;

    public DiscardPurchaseInvoiceDraftHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result> HandleAsync(DiscardPurchaseInvoiceDraftCommand command, CancellationToken cancellationToken)
    {
        var draft = await _context.PurchaseInvoiceDrafts
            .FirstOrDefaultAsync(d => d.Id == command.DraftId, cancellationToken);

        if (draft is null)
        {
            return Result.Failure(Error.NotFound("PurchaseInvoiceDraft.NotFound", $"مسودة الفاتورة '{command.DraftId}' غير موجودة."));
        }

        if (draft.Status != PurchaseInvoiceDraftStatus.PendingReview)
        {
            return Result.Failure(Error.BusinessRule(
                "PurchaseInvoiceDraft.NotPendingReview", "لا يمكن تجاهل مسودة فاتورة تم اعتمادها أو تجاهلها مسبقًا."));
        }

        draft.Discard();
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
