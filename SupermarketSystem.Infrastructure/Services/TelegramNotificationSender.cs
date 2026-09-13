using System.Net.Http.Json;
using System.Text.Json.Serialization;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Domain.Notifications;

namespace SupermarketSystem.Infrastructure.Services;

public static class NotificationSettingsKeys
{
    /// <summary>توكن بوت تلغرام. فاضي = القناة معطّلة، فشل هادئ بلا استثناء.</summary>
    public const string TelegramBotToken = "Notifications.TelegramBotToken";

    /// <summary>
    /// معرّف/معرّفات المحادثة (Chat ID) اللي الرسائل بتوصلها. رقم بلا هوية
    /// User مرتبطة — تلغرام بيراسل chat ID، لا مستخدم بالنظام. يدعم عدة
    /// مستلمين بقيمة واحدة مفصولة بفاصلة (مثلًا "123456,789012") — كل واحد
    /// بياخد محاولة إرسال منفصلة (best-effort لكل واحد لحاله، راجع تعليق
    /// TelegramNotificationSender.SendAsync).
    /// </summary>
    public const string TelegramChatId = "Notifications.TelegramChatId";
}

/// <summary>
/// يرسل عبر Telegram Bot API (endpoint عام: api.telegram.org، بلا استضافة
/// خاصة). التوكن ومعرّف المحادثة يُقرآن من الإعدادات وقت كل إرسال (عبر
/// ISettingsProvider المخزّن بالكاش أصلًا) — يعني تغيير التوكن من لوحة
/// الإعدادات بيصير فعّال فورًا، بلا إعادة تشغيل السيرفر.
///
/// المستقبِل هون هو "معرّف محادثة تلغرام" (أو أكتر من واحد مفصولين بفاصلة)،
/// لا مستخدم بالنظام (User) — هذا يتفادى عمدًا كل تعقيد "مين المدير" اللي
/// ما عنا حل له لسه بلا مصادقة حقيقية (راجع تعليق NotificationDispatcher).
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
        var chatIdsSetting = await _settingsProvider.GetStringAsync(NotificationSettingsKeys.TelegramChatId, null, cancellationToken);

        // فشل هادئ ومقصود — نفس مبدأ مزوّدي الذكاء الاصطناعي: مفتاح فاضي
        // يعني القناة "مش مفعّلة بعد"، لا خطأ إعداد يوقف شيء.
        if (string.IsNullOrWhiteSpace(botToken) || string.IsNullOrWhiteSpace(chatIdsSetting))
        {
            return (false, "قناة تلغرام غير مفعّلة (التوكن أو معرّف المحادثة غير مُعدّين بالإعدادات).");
        }

        var chatIds = chatIdsSetting
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct()
            .ToList();

        if (chatIds.Count == 0)
        {
            return (false, "قناة تلغرام غير مفعّلة (معرّف المحادثة فاضي بعد التقسيم على الفاصلة).");
        }

        // كل مستلم بمحاولة إرسال منفصلة، بأفضل جهد — فشل مستلم واحد ما
        // يمنع إرسال الباقي (راجع تعليق TelegramSettingsKeys.TelegramChatId).
        // النجاح الكلي = وصلت لمستلم واحد ع الأقل؛ أي فشل جزئي يُذكر برسالة
        // الخطأ حتى لو النتيجة الكلية "نجاح" (يظهر بسجل NotificationLog).
        var failures = new List<string>();
        var successCount = 0;

        foreach (var chatId in chatIds)
        {
            var (success, errorMessage) = await SendToSingleChatAsync(botToken, chatId, title, message, cancellationToken);

            if (success)
            {
                successCount++;
            }
            else
            {
                failures.Add($"{chatId}: {errorMessage}");
            }
        }

        var overallSuccess = successCount > 0;
        var combinedError = failures.Count == 0
            ? null
            : (chatIds.Count == 1
                ? failures[0]
                : $"فشل الإرسال لـ{failures.Count} من أصل {chatIds.Count} مستلم — " + string.Join(" | ", failures));

        return (overallSuccess, combinedError);
    }

    private async Task<(bool Success, string? ErrorMessage)> SendToSingleChatAsync(
        string botToken, string chatId, string title, string message, CancellationToken cancellationToken)
    {
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
