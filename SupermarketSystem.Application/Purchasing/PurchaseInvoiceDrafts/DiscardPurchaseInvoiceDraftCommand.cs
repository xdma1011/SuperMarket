using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.CashManagement;
using SupermarketSystem.Domain.Purchasing;

namespace SupermarketSystem.Application.Purchasing.PurchaseInvoiceDrafts;

public sealed record DiscardPurchaseInvoiceDraftCommand(Guid DraftId);

/// <summary>
/// لو المسودة كان عليها PaidNowAmount (كاش دُفع للمورد لحظة الرفع - راجع
/// CreatePurchaseInvoiceDraftFromImageHandler)، تجاهلها هون بيكتب حركة
/// عكسية بدرج الكاش تلقائيًا - وإلا كان الكاش يضل "طالع" بسجلات النظام
/// بلا أي فاتورة حقيقية تقابله، وتقفيل الصندوق يظهر زيادة وهمية دائمة.
/// </summary>
public sealed class DiscardPurchaseInvoiceDraftHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;

    public DiscardPurchaseInvoiceDraftHandler(
        IApplicationDbContext context, ICurrentUserContext currentUser, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
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

        if (draft.PaidNowAmount is { } paidNowAmount && draft.PaidNowPaymentMethodId is { } paidNowMethodId)
        {
            var affectsCashDrawer = await _context.PaymentMethods.AsNoTracking()
                .Where(pm => pm.Id == paidNowMethodId)
                .Select(pm => pm.AffectsCashDrawer)
                .FirstOrDefaultAsync(cancellationToken);

            if (affectsCashDrawer)
            {
                var userId = _currentUser.UserId
                    ?? throw new InvalidOperationException("لا يمكن تجاهل مسودة بلا هوية مستخدم مصادَق عليها.");

                _context.CashDrawerLogs.Add(new CashDrawerLog(
                    draft.BranchId,
                    CashDrawerMovementType.PurchasePaymentReversalCashIn,
                    paidNowAmount,
                    CashDrawerReferenceType.PurchaseInvoiceDraft,
                    draft.Id,
                    userId,
                    _dateTimeProvider.UtcNow));
            }
        }

        draft.Discard();
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
