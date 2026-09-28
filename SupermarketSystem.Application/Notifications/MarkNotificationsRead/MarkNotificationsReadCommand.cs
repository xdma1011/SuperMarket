using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Notifications;

namespace SupermarketSystem.Application.Notifications.MarkNotificationsRead;

// =====================================================================================
// لوحة التنبيهات الموحّدة (28/9/2026): تعليم كمقروء + أعداد غير المقروء لكل درجة. التنبيهات عامة (TargetUserId
// null = لكل الإدارة)، فالقراءة مشتركة - أي واحد من الإدارة بيعلّم، بتنعلّم عند الكل (محل صغير، إدارة وحدة).
// =====================================================================================

public sealed record MarkNotificationReadCommand(Guid NotificationId);

/// <summary>MaxSeverity: تعليم الكل لحد درجة معيّنة (مثلًا "علّم المعلومات كلها مقروءة") - null = الكل.</summary>
public sealed record MarkAllNotificationsReadCommand(NotificationSeverity? MaxSeverity);

public sealed record NotificationSummaryDto(int UnreadCritical, int UnreadWarning, int UnreadInfo)
{
    public int UnreadTotal => UnreadCritical + UnreadWarning + UnreadInfo;
}

public sealed class MarkNotificationReadHandler
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _dateTimeProvider;

    public MarkNotificationReadHandler(IApplicationDbContext context, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result> HandleAsync(MarkNotificationReadCommand command, CancellationToken cancellationToken)
    {
        var notification = await _context.Notifications
            .FirstOrDefaultAsync(n => n.Id == command.NotificationId && n.Channel == NotificationChannel.InApp, cancellationToken);
        if (notification is null)
        {
            return Result.Failure(Error.NotFound("Notification.NotFound", "التنبيه مش موجود."));
        }

        if (notification.Status != NotificationStatus.Read)
        {
            notification.MarkRead(_dateTimeProvider.UtcNow);
            await _context.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }
}

public sealed class MarkAllNotificationsReadHandler
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _dateTimeProvider;

    public MarkAllNotificationsReadHandler(IApplicationDbContext context, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<int> HandleAsync(MarkAllNotificationsReadCommand command, CancellationToken cancellationToken)
    {
        var unread = _context.Notifications
            .Where(n => n.Channel == NotificationChannel.InApp && n.Status != NotificationStatus.Read);
        if (command.MaxSeverity is { } max)
        {
            unread = unread.Where(n => n.Severity <= max);
        }

        var rows = await unread.ToListAsync(cancellationToken);
        var now = _dateTimeProvider.UtcNow;
        foreach (var notification in rows)
        {
            notification.MarkRead(now);
        }

        await _context.SaveChangesAsync(cancellationToken);
        return rows.Count;
    }
}

public sealed class GetNotificationSummaryHandler
{
    private readonly IApplicationDbContext _context;

    public GetNotificationSummaryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<NotificationSummaryDto> HandleAsync(CancellationToken cancellationToken)
    {
        var counts = await _context.Notifications.AsNoTracking()
            .Where(n => n.Channel == NotificationChannel.InApp && n.Status != NotificationStatus.Read)
            .GroupBy(n => n.Severity)
            .Select(g => new { Severity = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        int Of(NotificationSeverity severity) => counts.FirstOrDefault(c => c.Severity == severity)?.Count ?? 0;
        return new NotificationSummaryDto(Of(NotificationSeverity.Critical), Of(NotificationSeverity.Warning), Of(NotificationSeverity.Info));
    }
}
