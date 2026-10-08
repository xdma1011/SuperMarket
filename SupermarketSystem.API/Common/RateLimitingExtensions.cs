using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;

namespace SupermarketSystem.API.Common;

/// <summary>
/// Rate Limiting (2-أ بند 3، 8/10/2026) بمكتبة ASP.NET المدمجة (بلا حزمة جديدة، §1.2). الهدف: endpoints مجهولة (`customer-auth/request-otp`،
/// `verify-otp`، `POST /orders`) كانت مفتوحة للإغراق (رسائل تلغرام / طلبات توصيل وهمية)، والدخول (`auth/login`) محمي بقفل الحساب بس.
/// نافذة ثابتة دقيقة لكل IP. الحدود قابلة للضبط: `RateLimiting:Enabled` (افتراضي true)، `RateLimiting:{Login|Otp|Orders}:PermitLimit`.
/// تجاوز الحد = 429 برسالة عربية + `Retry-After`.
/// تنبيه نشر: خلف عاكس/نفق كل الطلبات ممكن تظهر بنفس عنوان IP (راجع ملاحظة الدخول بـAuthenticationEndpoints) - لازم ForwardedHeaders وقت النشر وإلا الحد بيصير مشترك.
/// لما الميزة مطفية بنسجّل نفس السياسات بلا حد (الـendpoints بتطلبها بالاسم، وغيابها بيكسر الطلب).
/// </summary>
public static class RateLimitingExtensions
{
    public const string Login = "login";
    public const string Otp = "otp";
    public const string Orders = "orders";

    public static IServiceCollection AddAppRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var enabled = configuration.GetValue("RateLimiting:Enabled", true);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            AddPolicy(options, Login, enabled, configuration.GetValue("RateLimiting:Login:PermitLimit", 30));
            AddPolicy(options, Otp, enabled, configuration.GetValue("RateLimiting:Otp:PermitLimit", 5));
            AddPolicy(options, Orders, enabled, configuration.GetValue("RateLimiting:Orders:PermitLimit", 10));

            options.OnRejected = async (context, cancellationToken) =>
            {
                var response = context.HttpContext.Response;
                response.StatusCode = StatusCodes.Status429TooManyRequests;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                await response.WriteAsJsonAsync(new
                {
                    type = "https://tools.ietf.org/html/rfc6585#section-4",
                    title = "طلبات كتير",
                    status = StatusCodes.Status429TooManyRequests,
                    detail = "عدد الطلبات تجاوز الحد المسموح بالدقيقة. استنى شوي وجرّب من جديد."
                }, cancellationToken);
            };
        });

        return services;
    }

    private static void AddPolicy(RateLimiterOptions options, string name, bool enabled, int permitLimit)
    {
        options.AddPolicy(name, httpContext => enabled
            ? RateLimitPartition.GetFixedWindowLimiter(
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = Math.Max(1, permitLimit),
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true
                })
            : RateLimitPartition.GetNoLimiter("disabled"));
    }
}
