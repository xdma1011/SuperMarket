using SupermarketSystem.API.Common;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Application.Notifications.GetNotifications;
using SupermarketSystem.Application.Notifications.MarkNotificationsRead;
using SupermarketSystem.Domain.Notifications;

namespace SupermarketSystem.API.Endpoints;

public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/notifications").WithTags("Notifications").RequirePermission(PermissionCodes.NotificationsView);

        // Polling — الواجهة بتستدعي هذا كل كم ثانية.
        group.MapGet("/", async (
            int? pageNumber, int? pageSize, string? search, string? sortBy, string? sortDirection,
            bool? unreadOnly,
            NotificationSeverity? minSeverity,
            DateTime? sinceUtc,
            NotificationSeverity? severity,
            GetNotificationsHandler handler,
            CancellationToken cancellationToken) =>
        {
            var paging = PagingBinder.Build(pageNumber, pageSize, search, sortBy, sortDirection);
            var result = await handler.HandleAsync(new GetNotificationsQuery(paging, unreadOnly ?? false, minSeverity, sinceUtc, severity), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetNotifications")
        .Produces<PagedResult<NotificationItemDto>>(StatusCodes.Status200OK);

        // لوحة التنبيهات الموحّدة (28/9/2026): أعداد غير المقروء لكل درجة (الجرس والرئيسية).
        group.MapGet("/summary", async (GetNotificationSummaryHandler handler, CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(cancellationToken)))
            .WithName("GetNotificationSummary")
            .Produces<NotificationSummaryDto>(StatusCodes.Status200OK);

        // تعليم كمقروء: صلاحية مختلفة عن المجموعة (Notifications.Manage) - برّا المجموعة عمدًا (§3.4: الفلاتر بتتراكم).
        // الكاشير بيشوف التنبيهات بس ما بيعلّم - القراءة مشتركة، فكان رح يقدر يخفي تنبيه عجز صندوقه.
        var manage = app.MapGroup("/api/v1/notifications").WithTags("Notifications").RequirePermission(PermissionCodes.NotificationsManage);

        manage.MapPost("/{notificationId:guid}/read", async (
                Guid notificationId, MarkNotificationReadHandler handler, CancellationToken cancellationToken) =>
                (await handler.HandleAsync(new MarkNotificationReadCommand(notificationId), cancellationToken)).ToHttpResult())
            .WithName("MarkNotificationRead")
            .ProducesProblem(StatusCodes.Status404NotFound);

        manage.MapPost("/read-all", async (
                NotificationSeverity? maxSeverity, MarkAllNotificationsReadHandler handler, CancellationToken cancellationToken) =>
                Results.Ok(new { marked = await handler.HandleAsync(new MarkAllNotificationsReadCommand(maxSeverity), cancellationToken) }))
            .WithName("MarkAllNotificationsRead")
            .WithSummary("تعليم كل التنبيهات مقروءة (أو لحد درجة معيّنة).");

        return app;
    }
}
