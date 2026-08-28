using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Domain.Notifications;

namespace SupermarketSystem.Application.Notifications.GetNotifications;

public sealed record GetNotificationsQuery(PagedRequest Paging, bool UnreadOnly);

public sealed record NotificationItemDto(
    Guid Id,
    string Title,
    string Message,
    NotificationChannel Channel,
    NotificationStatus Status,
    DateTime CreatedAtUtc,
    DateTime? ReadAtUtc);

/// <summary>
/// آلية Polling — الواجهة بتستدعي هذا كل كم ثانية بدل اتصال فوري
/// (SignalR/WebSocket). أبسط تنفيذًا، كافي لحجم الاستخدام الحالي؛ لو
/// احتجنا فورية حقيقية لاحقًا، هذا الـinterface بيضل نفسه، بس التطبيق
/// يتغيّر.
/// </summary>
public sealed class GetNotificationsHandler
{
    private readonly IApplicationDbContext _context;

    public GetNotificationsHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<NotificationItemDto>> HandleAsync(GetNotificationsQuery query, CancellationToken cancellationToken)
    {
        var paging = query.Paging.Normalized();

        // القناة الوحيدة اللي الواجهة بتعرضها بالـpolling هي InApp — قنوات
        // تانية (تلغرام) عندها سجل Notification منفصل خاص فيها للتدقيق،
        // بس مش المفروض تظهر بقائمة "إشعاراتي داخل النظام".
        var notifications = _context.Notifications.AsNoTracking()
            .Where(n => n.Channel == NotificationChannel.InApp);

        if (query.UnreadOnly)
        {
            notifications = notifications.Where(n => n.Status != NotificationStatus.Read);
        }

        notifications = notifications.OrderByDescending(n => n.CreatedAtUtc).ThenByDescending(n => n.Id);

        var totalCount = await notifications.CountAsync(cancellationToken);

        var items = await notifications
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Select(n => new NotificationItemDto(n.Id, n.Title, n.Message, n.Channel, n.Status, n.CreatedAtUtc, n.ReadAtUtc))
            .ToListAsync(cancellationToken);

        return new PagedResult<NotificationItemDto>(items, totalCount, paging.PageNumber, paging.PageSize);
    }
}
