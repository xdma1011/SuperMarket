using SupermarketSystem.API.Common;
using SupermarketSystem.Application.System.BootstrapAdmin;

namespace SupermarketSystem.API.Endpoints;

public static class SystemEndpoints
{
    public static IEndpointRouteBuilder MapSystemEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/system/bootstrap-admin", async (
            BootstrapAdminHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("BootstrapAdmin")
        .WithTags("System")
        .AllowAnonymous()
        .WithSummary("ينشئ فرعًا رئيسيًا ومستخدمًا إداريًا كامل الصلاحيات دفعة واحدة - يعمل مرة واحدة فقط على نظام فارغ تمامًا.")
        .Produces<BootstrapAdminResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }
}
