using SupermarketSystem.Domain.Notifications;

namespace SupermarketSystem.Application.Common.Interfaces;

/// <summary>
/// مُرسِل إشعار عبر قناة واحدة محددة (تلغرام مثلًا). كل قناة فعلية لها
/// تطبيق منفصل بـInfrastructure — بيسمح نضيف قناة جديدة (بريد، SMS)
/// بلا ما نلمس منطق التنسيق بـNotificationDispatcher.
///
/// الفشل هادئ بالتصميم: لو ما في إعداد (Token/ChatId) موجود بالإعدادات،
/// المُرسِل يرجّع فشل بسبب واضح، ما يرمي استثناء يوقف العملية الأساسية
/// (بيع، تقفيل صندوق، إلخ) — نفس مبدأ "لا نوقف البزنس" المطبَّق بكل مكان
/// تاني بالنظام.
/// </summary>
public interface INotificationSender
{
    NotificationChannel Channel { get; }

    Task<(bool Success, string? ErrorMessage)> SendAsync(string title, string message, CancellationToken cancellationToken);
}

/// <summary>
/// نقطة الدخول الوحيدة لإطلاق تنبيه من أي handler بالنظام. تُنشئ سجل
/// Notification (داخل النظام، للـpolling) دايمًا، وتحاول الإرسال عبر أي
/// قناة خارجية مفعّلة (تلغرام حاليًا) بأفضل جهد — فشل الإرسال الخارجي ما
/// بيمنع تسجيل الإشعار داخل النظام ولا بيوقف العملية اللي استدعته.
/// </summary>
public interface INotificationDispatcher
{
    Task NotifyAsync(string title, string message, CancellationToken cancellationToken);
}
