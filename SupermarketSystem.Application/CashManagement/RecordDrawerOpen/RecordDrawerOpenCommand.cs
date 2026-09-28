using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.CashManagement;

namespace SupermarketSystem.Application.CashManagement.RecordDrawerOpen;

/// <summary>
/// ClientRequestId/OccurredAtUtc (28/9/2026): الكاشير بيحفظ الفتحة محليًا (بلا نت) وبيبعتها لاحقًا بوقتها الفعلي.
/// بلاهم = السلوك القديم (وقت السيرفر، بلا idempotency).
/// </summary>
public sealed record RecordDrawerOpenCommand(Guid BranchId, string? Reason, Guid? ClientRequestId = null, DateTime? OccurredAtUtc = null);

public sealed record RecordDrawerOpenResponse(Guid DrawerOpenEventId, DateTime OccurredAtUtc, bool WasReplay = false);

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

        if (command.ClientRequestId is { } requestId)
        {
            var existing = await _context.DrawerOpenEvents.AsNoTracking()
                .Where(e => e.ClientRequestId == requestId)
                .Select(e => new { e.Id, e.OccurredAtUtc })
                .FirstOrDefaultAsync(cancellationToken);
            if (existing is not null)
            {
                return Result.Success(new RecordDrawerOpenResponse(existing.Id, existing.OccurredAtUtc, WasReplay: true));
            }
        }

        var now = _dateTimeProvider.UtcNow;
        var drawerOpen = new DrawerOpenEvent(
            command.BranchId, userId, EffectiveOccurredAt(command.OccurredAtUtc, now), command.Reason,
            command.ClientRequestId, recordedAtUtc: now);
        _context.DrawerOpenEvents.Add(drawerOpen);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (command.ClientRequestId is { } racedId)
        {
            // نفس الفتحة وصلت مرتين بنفس اللحظة (إرسال فوري + مزامنة خلفية) - الفهرس الفريد مسك التانية.
            _context.DrawerOpenEvents.Entry(drawerOpen).State = EntityState.Detached;
            var winner = await _context.DrawerOpenEvents.AsNoTracking()
                .Where(e => e.ClientRequestId == racedId)
                .Select(e => new { e.Id, e.OccurredAtUtc })
                .FirstOrDefaultAsync(cancellationToken);
            if (winner is null)
            {
                throw;
            }

            return Result.Success(new RecordDrawerOpenResponse(winner.Id, winner.OccurredAtUtc, WasReplay: true));
        }

        return Result.Success(new RecordDrawerOpenResponse(drawerOpen.Id, drawerOpen.OccurredAtUtc));
    }

    /// <summary>
    /// وقت الكاشير بيتقبل ضمن حدود معقولة: مش بالمستقبل (أكتر من 5 دقايق فرق ساعة) ولا أقدم من 30 يوم - برّاهم
    /// بنسجّل وقت الوصول بدل ما نرفض (§1.6: ما نوقف التسجيل - الفتحة بتضل معلّقة بالكاشير للأبد لو رفضنا).
    /// RecordedAtUtc بيضل يبين متى وصلت فعلًا.
    /// </summary>
    internal static DateTime EffectiveOccurredAt(DateTime? clientOccurredAtUtc, DateTime nowUtc)
    {
        if (clientOccurredAtUtc is not { } clientTime)
        {
            return nowUtc;
        }

        var utc = clientTime.Kind == DateTimeKind.Local ? clientTime.ToUniversalTime() : DateTime.SpecifyKind(clientTime, DateTimeKind.Utc);
        return utc > nowUtc.AddMinutes(5) || utc < nowUtc.AddDays(-30) ? nowUtc : utc;
    }
}
