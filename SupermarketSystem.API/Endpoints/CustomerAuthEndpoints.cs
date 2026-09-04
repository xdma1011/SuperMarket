using System.Text.Json.Serialization;
using SupermarketSystem.API.Common;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.CustomerAuth.LinkTelegramContact;
using SupermarketSystem.Application.CustomerAuth.RequestCustomerOtp;
using SupermarketSystem.Application.CustomerAuth.VerifyCustomerOtp;

namespace SupermarketSystem.API.Endpoints;

/// <summary>
/// تسجيل دخول تطبيق الزبائن عبر رقم الهاتف + تلغرام (راجع نقاش صاحب
/// المشروع، بديل عن الواتساب المدفوع). ثلاث خطوات:
/// 1) الزبون يفتح رابط البوت العميق ويشارك رقم هاتفه (Webhook هون يربطه).
/// 2) request-otp: يرسل كود عبر تلغرام لو الرقم مربوط، وإلا يرجّع رابط الربط.
/// 3) verify-otp: يتحقق من الكود ويصدر توكن هوية الزبون.
///
/// ⚠️ التوكن الصادر من verify-otp غير مفعَّل بعد بـPlaceOrder/GetCustomerOrders/
/// FileComplaint/RateOrder (لسا AllowAnonymous بمعامل موثوق بلا تحقق - راجع
/// تعليق OrderingEndpoints/CustomerEndpoints). هاي الخطوة القادمة، غير مبنية هون.
/// </summary>
public static class CustomerAuthEndpoints
{
    public static IEndpointRouteBuilder MapCustomerAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/customer-auth/request-otp", async (
            RequestCustomerOtpRequest request,
            RequestCustomerOtpHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new RequestCustomerOtpCommand(request.Phone), cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("RequestCustomerOtp")
        .WithTags("CustomerAuth")
        .AllowAnonymous()
        .WithSummary("يرسل كود تحقق عبر تلغرام لرقم مربوط مسبقًا، وإلا يرجّع رابط ربط البوت.")
        .Produces<RequestCustomerOtpResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        app.MapPost("/api/v1/customer-auth/verify-otp", async (
            VerifyCustomerOtpRequest request,
            VerifyCustomerOtpHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new VerifyCustomerOtpCommand(request.Phone, request.Code), cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("VerifyCustomerOtp")
        .WithTags("CustomerAuth")
        .AllowAnonymous()
        .WithSummary("يتحقق من كود OTP ويصدر توكن هوية الزبون (30 يوم).")
        .Produces<VerifyCustomerOtpResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        app.MapPost("/api/v1/telegram/webhook", async (
            TelegramUpdate update,
            ISettingsProvider settingsProvider,
            LinkTelegramContactHandler linkHandler,
            ITelegramBotClient botClient,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var expectedSecret = await settingsProvider.GetStringAsync(TelegramSettingsKeys.WebhookSecret, null, cancellationToken);
            var providedSecret = httpContext.Request.Headers["X-Telegram-Bot-Api-Secret-Token"].ToString();
            if (!string.IsNullOrWhiteSpace(expectedSecret) && providedSecret != expectedSecret)
            {
                return Results.Unauthorized();
            }

            var chatId = update.Message?.Chat?.Id;
            var contactPhone = update.Message?.Contact?.PhoneNumber;

            if (chatId is not null && !string.IsNullOrWhiteSpace(contactPhone))
            {
                await linkHandler.HandleAsync(new LinkTelegramContactCommand(chatId.Value.ToString(), contactPhone), cancellationToken);
                await botClient.SendMessageAsync(chatId.Value.ToString(), "تم ربط رقم هاتفك بنجاح ✅ رجع لتطبيق الطلبات لإكمال تسجيل الدخول.", cancellationToken);
            }
            else if (chatId is not null && update.Message?.Text == "/start")
            {
                await botClient.RequestContactAsync(chatId.Value.ToString(), "أهلًا بك 👋 اضغط الزر أدناه لمشاركة رقم هاتفك وربط حسابك.", cancellationToken);
            }

            return Results.Ok();
        })
        .WithName("TelegramWebhook")
        .WithTags("CustomerAuth")
        .AllowAnonymous()
        .WithSummary("Webhook تلغرام (Update entrypoint) - يربط رقم الهاتف بمحادثة تلغرام عند مشاركة جهة الاتصال.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    public sealed record RequestCustomerOtpRequest(string Phone);
    public sealed record VerifyCustomerOtpRequest(string Phone, string Code);

    public sealed record TelegramUpdate(
        [property: JsonPropertyName("message")] TelegramMessage? Message);

    public sealed record TelegramMessage(
        [property: JsonPropertyName("chat")] TelegramChat? Chat,
        [property: JsonPropertyName("contact")] TelegramContact? Contact,
        [property: JsonPropertyName("text")] string? Text);

    public sealed record TelegramChat(
        [property: JsonPropertyName("id")] long Id);

    public sealed record TelegramContact(
        [property: JsonPropertyName("phone_number")] string PhoneNumber);
}
