namespace SupermarketSystem.Application.Common.Interfaces;

/// <summary>
/// عميل بوت تلغرام - Infrastructure بينفّذها فعليًا (HTTP لـ api.telegram.org
/// باستخدام Telegram.BotToken من صفحة المفاتيح المقنَّعة). فشل هادئ إذا
/// التوكن غير مُعدّ (نفس مبدأ خدمات AI) - يرجّع false بدل ما يرمي استثناء
/// يوقف تدفق الطلب.
/// </summary>
public interface ITelegramBotClient
{
    Task<bool> SendMessageAsync(string chatId, string text, CancellationToken cancellationToken);

    /// <summary>يرسل زر "مشاركة رقم الهاتف" (keyboard request_contact) - يُستخدم بعد /start.</summary>
    Task<bool> RequestContactAsync(string chatId, string text, CancellationToken cancellationToken);
}
