namespace SupermarketSystem.Application.Common.Interfaces;

/// <summary>
/// إرسال إشعار Push لتوكن جهاز واحد (FCM). Infrastructure بينفّذها فعليًا
/// (Firebase.ServiceAccountJson من المفاتيح المقنَّعة). فشل هادئ لو
/// الاعتماد غير مُعدّ - نفس مبدأ كل تكامل خارجي بالمشروع.
/// </summary>
public interface IPushNotificationSender
{
    Task<bool> SendAsync(string deviceToken, string title, string body, CancellationToken cancellationToken);
}
