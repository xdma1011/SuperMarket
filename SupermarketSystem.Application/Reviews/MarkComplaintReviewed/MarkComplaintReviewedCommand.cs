using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;

namespace SupermarketSystem.Application.Reviews.MarkComplaintReviewed;

public sealed record MarkComplaintReviewedCommand(Guid ComplaintId);

/// <summary>نفس نمط MarkStockMovementReviewedHandler بالضبط - يعلّم شكوى كمحلولة.</summary>
public sealed class MarkComplaintReviewedHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;

    public MarkComplaintReviewedHandler(IApplicationDbContext context, ICurrentUserContext currentUser, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result> HandleAsync(MarkComplaintReviewedCommand command, CancellationToken cancellationToken)
    {
        var complaint = await _context.Complaints.FirstOrDefaultAsync(c => c.Id == command.ComplaintId, cancellationToken);
        if (complaint is null)
        {
            return Result.Failure(Error.NotFound("Complaint.NotFound", $"الشكوى '{command.ComplaintId}' غير موجودة."));
        }

        var userId = _currentUser.UserId
            ?? throw new InvalidOperationException("لا يمكن تعليم شكوى بلا هوية مستخدم مصادَق عليها.");

        try
        {
            complaint.MarkResolved(userId, _dateTimeProvider.UtcNow);
        }
        catch (Domain.Common.DomainException ex)
        {
            return Result.Failure(Error.Conflict("Complaint.AlreadyResolved", ex.Message));
        }

        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
