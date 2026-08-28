using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Identity;

namespace SupermarketSystem.Application.Sales.MarkReturnReviewed;

public sealed record MarkReturnReviewedCommand(Guid ReturnInvoiceId);

public sealed record MarkReturnReviewedResponse(Guid ReturnInvoiceId, string InvoiceNumber, DateTime ReviewedAtUtc);

/// <summary>
/// إجراء إداري لاحق بحت — صاحب المحل بيفتح سجل المرتجعات، بيراجع إرجاع
/// صار قبل يوم أو أكتر، وبيحط عليه "تمت المراجعة".
///
/// ما بيغيّر أي شي مالي: لا المبالغ، لا المخزون، لا حالة الإرجاع. الإرجاع
/// بيضل ظاهر كإرجاع للأبد بكل التقارير — العلامة معلومة *مضافة*، لا
/// بديلة ولا مخفية.
///
/// وما بتمنع الإرجاع ولا بتأخره: الإرجاع نفسه صار وخلص فورًا وقت وقوفه
/// عند الكاشير. هذا مجرد أثر إداري يقول "شفتها".
/// </summary>
public sealed class MarkReturnReviewedHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;

    public MarkReturnReviewedHandler(
        IApplicationDbContext context, ICurrentUserContext currentUser, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<MarkReturnReviewedResponse>> HandleAsync(
        MarkReturnReviewedCommand command, CancellationToken cancellationToken)
    {
        var returnInvoice = await _context.ReturnInvoices
            .FirstOrDefaultAsync(r => r.Id == command.ReturnInvoiceId, cancellationToken);

        if (returnInvoice is null)
        {
            return Result.Failure<MarkReturnReviewedResponse>(
                Error.NotFound("Return.NotFound", $"الإرجاع '{command.ReturnInvoiceId}' غير موجود."));
        }

        if (returnInvoice.ReviewedAtUtc is not null)
        {
            return Result.Failure<MarkReturnReviewedResponse>(Error.Conflict(
                "Return.AlreadyReviewed",
                $"هذا الإرجاع مُراجَع مسبقًا بتاريخ {returnInvoice.ReviewedAtUtc:yyyy-MM-dd}."));
        }

        var actorUserId = _currentUser.UserId ?? User.SystemUserId;
        var reviewedAtUtc = _dateTimeProvider.UtcNow;

        returnInvoice.MarkReviewed(actorUserId, reviewedAtUtc);

        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(new MarkReturnReviewedResponse(
            returnInvoice.Id, returnInvoice.InvoiceNumber, reviewedAtUtc));
    }
}
