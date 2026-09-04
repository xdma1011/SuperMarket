namespace SupermarketSystem.Application.Common.Interfaces;

/// <summary>
/// طبقة فوق IPushNotificationSender - تجلب كل توكنات أجهزة الزبون
/// وترسل للكل (زبون ممكن يكون عنده أكثر من جهاز، راجع CustomerDeviceToken).
/// تُستدعى من AcceptOrder/RejectOrder/CompleteOrder عند تغيّر حالة الطلب.
/// فشل إرسال جهاز واحد ما يوقف البقية ولا يفشّل العملية الأصلية (بيع/قبول/رفض) -
/// نفس مبدأ "سماح مع مراجعة": إشعار فاشل مش سبب لإيقاف عملية حقيقية.
/// </summary>
public interface ICustomerPushNotifier
{
    Task NotifyOrderStatusChangedAsync(Guid customerId, string title, string body, CancellationToken cancellationToken);
}
