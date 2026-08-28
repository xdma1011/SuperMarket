using SupermarketSystem.API.Common;
using SupermarketSystem.Application.Authentication.GetActiveSessions;
using SupermarketSystem.Application.Authentication.GetMyPermissions;
using SupermarketSystem.Application.Authentication.Login;
using SupermarketSystem.Application.Authentication.Logout;
using SupermarketSystem.Application.Authentication.RefreshToken;
using SupermarketSystem.Application.Authentication.RevokeSession;
using SupermarketSystem.Application.Branches.GetPublicBranches;
using SupermarketSystem.Application.Payments.GetPaymentMethods;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Domain.Identity;

namespace SupermarketSystem.API.Endpoints;

public static class AuthenticationEndpoints
{
    public static IEndpointRouteBuilder MapAuthenticationEndpoints(this IEndpointRouteBuilder app)
    {
        // مُعفاة صراحةً من FallbackPolicy — الطلب هون *قبل* ما يكون في
        // هوية إطلاقًا. بلا هذا السطر، حتى تسجيل الدخول نفسه كان رح يُرفض
        // بـ401 (السياسة الافتراضية بتغطي كل شي ما لم يُعفَ صراحةً).
        var group = app.MapGroup("/api/v1/auth").WithTags("Authentication").AllowAnonymous();

        group.MapGet("/branches", async (
            GetPublicBranchesHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetPublicBranches")
        .WithSummary("قائمة الفروع (اسم فقط) لتعبئة قائمة اختيار الفرع بصفحة تسجيل الدخول - قبل وجود أي توكن.")
        .Produces<IReadOnlyList<PublicBranchDto>>(StatusCodes.Status200OK);

        group.MapPost("/login", async (
            LoginRequest request,
            HttpContext httpContext,
            LoginHandler handler,
            CancellationToken cancellationToken) =>
        {
            // الـIP ومعرّف الجهاز يُلتقطان من الطلب نفسه، لا من جسم الطلب —
            // لو خلّينا العميل يرسلهم، بيصيروا بلا قيمة أمنية إطلاقًا (أي
            // مهاجم بيرسل اللي بدّه). القيمة الوحيدة لتسجيلهم إنهم مأخوذان
            // من طبقة النقل، مش من كلام العميل.
            //
            // ملاحظة تشغيلية: خلف عاكس (nginx/IIS) RemoteIpAddress بيصير
            // عنوان العاكس نفسه لكل الطلبات ما لم تُضبط ForwardedHeaders —
            // إعداد نشر، مش خلل بالكود، بس لازم ينتبهله وقت النشر.
            var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();
            var deviceInfo = httpContext.Request.Headers.UserAgent.ToString();

            var command = new LoginCommand(
                request.Username,
                request.Password,
                request.AppType,
                request.BranchId,
                ipAddress,
                string.IsNullOrWhiteSpace(deviceInfo) ? null : deviceInfo);

            var result = await handler.HandleAsync(command, cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("Login")
        .WithSummary("تسجيل دخول: يتحقق من بيانات الاعتماد، ينشئ جلسة، ويسحب أي جلسة قائمة لنفس المستخدم بنفس نوع التطبيق.")
        .Produces<LoginResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/refresh", async (
            RefreshTokenRequest request,
            RefreshTokenHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new RefreshTokenCommand(request.RefreshToken), cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("RefreshToken")
        .WithSummary("يجدّد الجلسة: يصدر access token جديدًا ويدوّر refresh token — التوكن القديم يصير غير صالح فورًا بعد أول استخدام.")
        .Produces<RefreshTokenResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/logout", async (
            LogoutRequest request,
            LogoutHandler handler,
            CancellationToken cancellationToken) =>
        {
            await handler.HandleAsync(new LogoutCommand(request.RefreshToken), cancellationToken);
            // دائمًا 204، بغض النظر عن حالة التوكن — راجع تعليق LogoutHandler.
            return Results.NoContent();
        })
        .WithName("Logout")
        .WithSummary("خروج طوعي — يبطل الجلسة المرتبطة بتوكن التجديد المُرسَل. عملية هادئة دائمًا (204)، بلا كشف حالة التوكن.")
        .Produces(StatusCodes.Status204NoContent);

        // === إدارة الجلسات (إجراءات إدارية) ===
        var sessionsGroup = app.MapGroup("/api/v1/auth/sessions")
            .WithTags("Authentication")
            .RequirePermission(PermissionCodes.SessionsManage);

        sessionsGroup.MapGet("/", async (
            int? pageNumber, int? pageSize, string? search, string? sortBy, string? sortDirection,
            Guid? userId,
            GetActiveSessionsHandler handler,
            CancellationToken cancellationToken) =>
        {
            var paging = PagingBinder.Build(pageNumber, pageSize, search, sortBy, sortDirection);
            var result = await handler.HandleAsync(new GetActiveSessionsQuery(paging, userId), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetActiveSessions")
        .WithSummary("قائمة الجلسات الفعّالة حاليًا (لم تُبطَل ولم تنتهِ) — تُستخدم لملاحظة IP/جهاز غريب واتخاذ قرار الإيقاف.")
        .Produces<PagedResult<ActiveSessionItemDto>>(StatusCodes.Status200OK);

        sessionsGroup.MapPost("/{sessionId:guid}/revoke", async (
            Guid sessionId,
            RevokeSessionHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new RevokeSessionCommand(sessionId), cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("RevokeSession")
        .WithSummary("إيقاف جلسة محددة فورًا وإداريًا — مثلًا عند إنهاء خدمة موظف أو اشتباه بجلسة مسروقة.")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        // بلا AllowAnonymous وبلا RequirePermission — بيرث السياسة
        // الافتراضية (FallbackPolicy: يتطلب مصادقة بس، بلا صلاحية محددة).
        // أي مستخدم مسجَّل دخول يقدر يشوف صلاحياته هو نفسه، بغض النظر شو
        // كانت.
        app.MapGet("/api/v1/auth/my-permissions", async (
            GetMyPermissionsHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetMyPermissions")
        .WithTags("Authentication")
        .WithSummary("صلاحيات المستخدم الحالي — أساس إخفاء عناصر الواجهة، لا الحماية الفعلية (الحماية دائمًا بالباك إند).")
        .Produces<MyPermissionsResponse>(StatusCodes.Status200OK);

        app.MapGet("/api/v1/currencies", () => Results.Ok(CurrencyCatalog.All))
            .WithName("GetCurrencies")
            .WithTags("System")
            .WithSummary("قائمة العملات المدعومة - JOD وUSD مبدئيًا.")
            .Produces<IReadOnlyList<CurrencyInfo>>(StatusCodes.Status200OK);

        app.MapGet("/api/v1/common-units", () => Results.Ok(CommonUnits.All))
            .WithName("GetCommonUnits")
            .WithTags("System")
            .WithSummary("قائمة وحدات شائعة كاقتراحات - ProductUnit.UnitName يضل نصًا حرًا، هذي بس تسهّل الاختيار.")
            .Produces<IReadOnlyList<string>>(StatusCodes.Status200OK);

        app.MapGet("/api/v1/payment-methods", async (
            GetPaymentMethodsHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetPaymentMethods")
        .WithTags("System")
        .WithSummary("طرق الدفع الفعّالة - أساس أي بيع أو استرجاع بالفرونت إند.")
        .Produces<IReadOnlyList<PaymentMethodDto>>(StatusCodes.Status200OK);

        return app;
    }

    public sealed record RefreshTokenRequest(string RefreshToken);
    public sealed record LogoutRequest(string RefreshToken);

    /// <summary>
    /// الـIP والجهاز مقصود إنهم *مش* هون — يُلتقطان من الطلب نفسه لا من
    /// كلام العميل (راجع التعليق داخل الـendpoint).
    /// </summary>
    public sealed record LoginRequest(string Username, string Password, ClientAppType AppType, Guid? BranchId);
}
