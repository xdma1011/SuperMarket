using SupermarketSystem.API.Common;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.System.BootstrapAdmin;
using SupermarketSystem.Application.System.GetAdminSettings;
using SupermarketSystem.Application.System.UpdateAdminSetting;

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

        app.MapGet("/api/v1/system/admin-settings", async (
            GetAdminSettingsHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetAdminSettings")
        .WithTags("System")
        .RequirePermission(PermissionCodes.SystemSettingsManage)
        .WithSummary("يجلب قائمة الإعدادات الحسّاسة القابلة للتعديل من صفحة الإعدادات (whitelist صريحة).")
        .Produces<GetAdminSettingsResponse>(StatusCodes.Status200OK);

        app.MapPut("/api/v1/system/admin-settings", async (
            UpdateAdminSettingRequest request,
            UpdateAdminSettingHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new UpdateAdminSettingCommand(request.Key, request.Value), cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("UpdateAdminSetting")
        .WithTags("System")
        .RequirePermission(PermissionCodes.SystemSettingsManage)
        .WithSummary("يعدّل قيمة إعداد حسّاس واحد (Key ضمن whitelist صريحة فقط).")
        .Produces<UpdateAdminSettingResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    public sealed record UpdateAdminSettingRequest(string Key, string Value);
}
