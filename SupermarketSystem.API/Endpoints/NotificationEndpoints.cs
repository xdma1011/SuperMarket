using SupermarketSystem.API.Common;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Application.Notifications.GetNotifications;

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
            GetNotificationsHandler handler,
            CancellationToken cancellationToken) =>
        {
            var paging = PagingBinder.Build(pageNumber, pageSize, search, sortBy, sortDirection);
            var result = await handler.HandleAsync(new GetNotificationsQuery(paging, unreadOnly ?? false), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetNotifications")
        .Produces<PagedResult<NotificationItemDto>>(StatusCodes.Status200OK);

        return app;
    }
}
