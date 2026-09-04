using System.Net.Http.Json;
using System.Text.Json.Serialization;
using SupermarketSystem.Application.Common.Interfaces;

namespace SupermarketSystem.Infrastructure.Services;

/// <summary>
/// بوت تلغرام مخصَّص لتسجيل دخول الزبائن (تطبيق الطلبات) - منفصل عمدًا عن
/// TelegramNotificationSender (بوت تنبيهات الإدارة، Notifications.TelegramBotToken):
/// هذا البوت يستقبل رسائل واردة (Webhook: مشاركة جهة اتصال) وليس بس يرسل،
/// وله جمهور بعدد الزبائن لا مدير واحد ثابت. راجع TelegramSettingsKeys.BotToken.
/// </summary>
public sealed class TelegramBotClient : ITelegramBotClient
{
    private const string TelegramApiBaseUrl = "https://api.telegram.org";

    private readonly HttpClient _httpClient;
    private readonly ISettingsProvider _settingsProvider;

    public TelegramBotClient(HttpClient httpClient, ISettingsProvider settingsProvider)
    {
        _httpClient = httpClient;
        _settingsProvider = settingsProvider;
    }

    public async Task<bool> SendMessageAsync(string chatId, string text, CancellationToken cancellationToken)
    {
        var botToken = await _settingsProvider.GetStringAsync(TelegramSettingsKeys.BotToken, null, cancellationToken);
        if (string.IsNullOrWhiteSpace(botToken))
        {
            // فشل هادئ ومقصود - نفس مبدأ كل تكامل خارجي بالمشروع: توكن غير
            // مُعدّ يعني "القناة مش مفعّلة بعد"، لا استثناء يوقف الطلب.
            return false;
        }

        try
        {
            var url = $"{TelegramApiBaseUrl}/bot{botToken}/sendMessage";
            var response = await _httpClient.PostAsJsonAsync(url, new TelegramSendMessageRequest(chatId, text, null), cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> RequestContactAsync(string chatId, string text, CancellationToken cancellationToken)
    {
        var botToken = await _settingsProvider.GetStringAsync(TelegramSettingsKeys.BotToken, null, cancellationToken);
        if (string.IsNullOrWhiteSpace(botToken))
        {
            return false;
        }

        try
        {
            var url = $"{TelegramApiBaseUrl}/bot{botToken}/sendMessage";
            var keyboard = new TelegramReplyKeyboard(
                Keyboard: new[] { new[] { new TelegramKeyboardButton("مشاركة رقم الهاتف 📱", true) } },
                ResizeKeyboard: true,
                OneTimeKeyboard: true);

            var response = await _httpClient.PostAsJsonAsync(
                url, new TelegramSendMessageWithKeyboardRequest(chatId, text, keyboard), cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private sealed record TelegramSendMessageRequest(
        [property: JsonPropertyName("chat_id")] string ChatId,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("parse_mode")] string? ParseMode);

    private sealed record TelegramSendMessageWithKeyboardRequest(
        [property: JsonPropertyName("chat_id")] string ChatId,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("reply_markup")] TelegramReplyKeyboard ReplyMarkup);

    private sealed record TelegramReplyKeyboard(
        [property: JsonPropertyName("keyboard")] TelegramKeyboardButton[][] Keyboard,
        [property: JsonPropertyName("resize_keyboard")] bool ResizeKeyboard,
        [property: JsonPropertyName("one_time_keyboard")] bool OneTimeKeyboard);

    private sealed record TelegramKeyboardButton(
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("request_contact")] bool RequestContact);
}
