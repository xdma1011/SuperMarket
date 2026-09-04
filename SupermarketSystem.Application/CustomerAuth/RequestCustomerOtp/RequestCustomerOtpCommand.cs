using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Customers;

namespace SupermarketSystem.Application.CustomerAuth.RequestCustomerOtp;

public sealed record RequestCustomerOtpCommand(string Phone);

public sealed record RequestCustomerOtpResponse(bool TelegramLinked, string? TelegramDeepLink);

/// <summary>
/// خطوة 1 من تسجيل دخول الزبون: لو رقمه مربوط بمحادثة تلغرام (TelegramChatLink،
/// راجع LinkTelegramContactHandler) يولّد كود ويرسله عبرها. لو غير مربوط
/// بعد، يرجّع رابط البوت العميق (t.me/username?start=...) ليفتحه الزبون
/// ويشارك رقم هاتفه أول مرة - بلا كود بهاي الحالة، الزبون لازم يعيد
/// المحاولة بعد الربط.
///
/// "سماح مع مراجعة" ما ينطبق هون - هاي عملية مصادقة، لا عملية بيع أو
/// مخزون، فالرفض الصريح (رقم غير مربوط) هو السلوك الصحيح والمتوقَّع.
/// </summary>
public sealed class RequestCustomerOtpHandler
{
    private const int CodeLifetimeMinutes = 5;

    private readonly IApplicationDbContext _context;
    private readonly ITelegramBotClient _telegramBotClient;
    private readonly ISettingsProvider _settingsProvider;
    private readonly IDateTimeProvider _dateTimeProvider;

    public RequestCustomerOtpHandler(
        IApplicationDbContext context,
        ITelegramBotClient telegramBotClient,
        ISettingsProvider settingsProvider,
        IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _telegramBotClient = telegramBotClient;
        _settingsProvider = settingsProvider;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<RequestCustomerOtpResponse>> HandleAsync(
        RequestCustomerOtpCommand command, CancellationToken cancellationToken)
    {
        var phone = command.Phone.Trim();
        if (string.IsNullOrWhiteSpace(phone))
        {
            return Result.Failure<RequestCustomerOtpResponse>(
                Error.Validation("CustomerOtp.PhoneRequired", "رقم الهاتف مطلوب."));
        }

        var chatLink = await _context.TelegramChatLinks.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Phone == phone, cancellationToken);

        if (chatLink is null)
        {
            var botUsername = await _settingsProvider.GetStringAsync(TelegramSettingsKeys.BotUsername, null, cancellationToken);
            var deepLink = string.IsNullOrWhiteSpace(botUsername) ? null : $"https://t.me/{botUsername}?start=link";
            return Result.Success(new RequestCustomerOtpResponse(TelegramLinked: false, deepLink));
        }

        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        var codeHash = HashCode(code);
        var now = _dateTimeProvider.UtcNow;

        _context.CustomerOtpCodes.Add(new CustomerOtpCode(phone, codeHash, now, now.AddMinutes(CodeLifetimeMinutes)));
        await _context.SaveChangesAsync(cancellationToken);

        await _telegramBotClient.SendMessageAsync(
            chatLink.ChatId, $"كود تسجيل الدخول: {code}\nصالح لمدة {CodeLifetimeMinutes} دقائق.", cancellationToken);

        return Result.Success(new RequestCustomerOtpResponse(TelegramLinked: true, TelegramDeepLink: null));
    }

    private static string HashCode(string code)
        => Convert.ToBase64String(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(code)));
}
