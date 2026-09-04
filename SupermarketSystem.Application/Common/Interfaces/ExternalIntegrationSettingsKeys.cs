namespace SupermarketSystem.Application.Common.Interfaces;

/// <summary>
/// مفاتيح إعدادات تكاملات خارجية (أسرار) - نفس نمط InvoiceOcrSettingsKeys
/// بالضبط. مفتاح فاضي = القناة معطّلة، فشل هادئ (نفس مبدأ AI). هاي
/// المفاتيح تُدار حصرًا من صفحة "المفاتيح المقنَّعة" - لا تظهر أبدًا
/// كنص بأي واجهة، بعكس صفحة الإعدادات العادية.
/// </summary>
public static class TelegramSettingsKeys
{
    /// <summary>توكن بوت تلغرام (من BotFather) - يُستخدم لإرسال أكواد تسجيل الدخول (OTP) لتطبيق الزبائن.</summary>
    public const string BotToken = "Telegram.BotToken";

    /// <summary>اسم مستخدم البوت (بدون @) - علني بطبيعته (يظهر برابط t.me/username)، لذا مُدار من صفحة الإعدادات العادية لا المقنَّعة.</summary>
    public const string BotUsername = "Telegram.BotUsername";

    /// <summary>سرّ التحقق من Webhook (secret_token عند setWebhook) - يمنع أي طرف غير تلغرام من استدعاء endpoint الربط بأرقام هواتف مزوَّرة.</summary>
    public const string WebhookSecret = "Telegram.WebhookSecret";
}

public static class FirebaseSettingsKeys
{
    /// <summary>محتوى ملف Service Account JSON كامل (من Firebase Console) - يُستخدم لإرسال Push Notifications.</summary>
    public const string ServiceAccountJson = "Firebase.ServiceAccountJson";
}
