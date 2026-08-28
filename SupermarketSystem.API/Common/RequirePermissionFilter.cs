using SupermarketSystem.Application.Common.Interfaces;

namespace SupermarketSystem.API.Common;

/// <summary>
/// فلتر endpoint بسيط — يقرأ ICurrentUserContext.UserId (اللي RealCurrentUserContext
/// بيملأها من الـclaims، خطوة 8)، وبيفحص الصلاحية عبر IPermissionChecker
/// الحي (خطوة 9). الاثنان يُطلبان من الـDI مباشرة (لا حقن بالمنشئ) —
/// endpoint filters بـMinimal APIs بتُبنى مرة وحدة وقت الإقلاع، فمينفعش
/// تاخد خدمات Scoped بالمنشئ.
/// </summary>
public sealed class RequirePermissionFilter : IEndpointFilter
{
    private readonly string _permissionCode;

    public RequirePermissionFilter(string permissionCode)
    {
        _permissionCode = permissionCode;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var currentUser = context.HttpContext.RequestServices.GetRequiredService<ICurrentUserContext>();

        if (currentUser.UserId is not { } userId)
        {
            // بلا هوية إطلاقًا — 401، لا 403: الفرق مهم (401 = "ما بعرفك"،
            // 403 = "بعرفك بس ممنوع").
            return Results.Unauthorized();
        }

        var permissionChecker = context.HttpContext.RequestServices.GetRequiredService<IPermissionChecker>();
        var hasPermission = await permissionChecker.HasPermissionAsync(
            userId, _permissionCode, context.HttpContext.RequestAborted);

        if (!hasPermission)
        {
            return Results.Problem(
                title: "Forbidden",
                detail: $"لا تملك صلاحية '{_permissionCode}' اللازمة لهذه العملية.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        return await next(context);
    }
}

public static class EndpointFilterExtensions
{
    public static RouteHandlerBuilder RequirePermission(this RouteHandlerBuilder builder, string permissionCode)
        => builder.AddEndpointFilter(new RequirePermissionFilter(permissionCode));

    public static RouteGroupBuilder RequirePermission(this RouteGroupBuilder builder, string permissionCode)
        => builder.AddEndpointFilter(new RequirePermissionFilter(permissionCode));
}
