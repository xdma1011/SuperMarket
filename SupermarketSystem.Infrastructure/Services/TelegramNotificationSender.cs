using System.Net.Http.Json;
using System.Text.Json.Serialization;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Domain.Notifications;

namespace SupermarketSystem.Infrastructure.Services;

public static class NotificationSettingsKeys
{
    /// <summary>توكن بوت تلغرام. فاضي = القناة معطّلة، فشل هادئ بلا استثناء.</summary>
    public const string TelegramBotToken = "Notifications.TelegramBotToken";

    /// <summary>معرّف المحادثة (Chat ID) اللي الرسائل بتوصلها. رقم بلا هوية User مرتبطة — تلغرام بيراسل chat ID، لا مستخدم بالنظام.</summary>
    public const string TelegramChatId = "Notifications.TelegramChatId";
}

/// <summary>
/// يرسل عبر Telegram Bot API (endpoint عام: api.telegram.org، بلا استضافة
/// خاصة). التوكن ومعرّف المحادثة يُقرآن من الإعدادات وقت كل إرسال (عبر
/// ISettingsProvider المخزّن بالكاش أصلًا) — يعني تغيير التوكن من لوحة
/// الإعدادات بيصير فعّال فورًا، بلا إعادة تشغيل السيرفر.
///
/// المستقبِل هون هو "معرّف محادثة تلغرام"، لا مستخدم بالنظام (User) — هذا
/// يتفادى عمدًا كل تعقيد "مين المدير" اللي ما عنا حل له لسه بلا مصادقة
/// حقيقية (راجع تعليق NotificationDispatcher).
/// </summary>
public sealed class TelegramNotificationSender : INotificationSender
{
    private const string TelegramApiBaseUrl = "https://api.telegram.org";

    private readonly HttpClient _httpClient;
    private readonly ISettingsProvider _settingsProvider;

    public NotificationChannel Channel => NotificationChannel.Telegram;

    public TelegramNotificationSender(HttpClient httpClient, ISettingsProvider settingsProvider)
    {
        _httpClient = httpClient;
        _settingsProvider = settingsProvider;
    }

    public async Task<(bool Success, string? ErrorMessage)> SendAsync(
        string title, string message, CancellationToken cancellationToken)
    {
        var botToken = await _settingsProvider.GetStringAsync(NotificationSettingsKeys.TelegramBotToken, null, cancellationToken);
        var chatId = await _settingsProvider.GetStringAsync(NotificationSettingsKeys.TelegramChatId, null, cancellationToken);

        // فشل هادئ ومقصود — نفس مبدأ مزوّدي الذكاء الاصطناعي: مفتاح فاضي
        // يعني القناة "مش مفعّلة بعد"، لا خطأ إعداد يوقف شيء.
        if (string.IsNullOrWhiteSpace(botToken) || string.IsNullOrWhiteSpace(chatId))
        {
            return (false, "قناة تلغرام غير مفعّلة (التوكن أو معرّف المحادثة غير مُعدّين بالإعدادات).");
        }

        try
        {
            var url = $"{TelegramApiBaseUrl}/bot{botToken}/sendMessage";
            var text = $"*{EscapeMarkdown(title)}*\n{EscapeMarkdown(message)}";

            var response = await _httpClient.PostAsJsonAsync(
                url,
                new TelegramSendMessageRequest(chatId, text, ParseMode: "MarkdownV2"),
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return (true, null);
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return (false, $"Telegram API رجّعت {(int)response.StatusCode}: {body}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>MarkdownV2 بتلغرام بتتطلب escape لأحرف معيّنة، وإلا الرسالة بترفض كاملة.</summary>
    private static string EscapeMarkdown(string text)
    {
        const string specialChars = "_*[]()~`>#+-=|{}.!";
        var result = text;
        foreach (var c in specialChars)
        {
            result = result.Replace(c.ToString(), $"\\{c}");
        }
        return result;
    }

    private sealed record TelegramSendMessageRequest(
        [property: JsonPropertyName("chat_id")] string ChatId,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("parse_mode")] string ParseMode);
}
