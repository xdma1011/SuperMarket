using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.CashManagement;

namespace SupermarketSystem.Application.CashManagement.RecordDrawerOpen;

public sealed record RecordDrawerOpenCommand(Guid BranchId, string? Reason);

public sealed record RecordDrawerOpenResponse(Guid DrawerOpenEventId, DateTime OccurredAtUtc);

/// <summary>
/// زر "فتح الصندوق" بالكاشير: بيسجّل فتح الدرج بلا بيع (راجع DrawerOpenEvent). تسجيل بس - ما
/// بيفتح درج حقيقي ولا بيأثر على أي مبلغ. الكاشير بيسجّل لفرعه بس (زي البيع).
/// </summary>
public sealed class RecordDrawerOpenHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;

    public RecordDrawerOpenHandler(IApplicationDbContext context, ICurrentUserContext currentUser, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<RecordDrawerOpenResponse>> HandleAsync(RecordDrawerOpenCommand command, CancellationToken cancellationToken)
    {
        if (command.BranchId == Guid.Empty)
        {
            return Result.Failure<RecordDrawerOpenResponse>(Error.Validation("DrawerOpen.BranchRequired", "الفرع مطلوب."));
        }

        if (command.Reason is { Length: > DrawerOpenEvent.MaxReasonLength })
        {
            return Result.Failure<RecordDrawerOpenResponse>(
                Error.Validation("DrawerOpen.ReasonTooLong", $"السبب أطول من {DrawerOpenEvent.MaxReasonLength} حرف."));
        }

        if (!_currentUser.IsCrossBranchAccessAllowed && _currentUser.BranchId != command.BranchId)
        {
            return Result.Failure<RecordDrawerOpenResponse>(
                Error.Forbidden("DrawerOpen.BranchNotAllowed", "ما بتقدر تسجّل فتح صندوق لفرع غير فرعك."));
        }

        var userId = _currentUser.UserId
            ?? throw new InvalidOperationException("لا يمكن تسجيل فتح صندوق بلا هوية مستخدم مصادَق عليها.");

        var drawerOpen = new DrawerOpenEvent(command.BranchId, userId, _dateTimeProvider.UtcNow, command.Reason);
        _context.DrawerOpenEvents.Add(drawerOpen);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(new RecordDrawerOpenResponse(drawerOpen.Id, drawerOpen.OccurredAtUtc));
    }
}
