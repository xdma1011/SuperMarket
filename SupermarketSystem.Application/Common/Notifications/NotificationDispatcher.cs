using Microsoft.Extensions.Logging;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Domain.Notifications;

namespace SupermarketSystem.Application.Common.Notifications;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════
/// تحذير استخدام مهم — لازم يُستدعى بعد نجاح commit للعملية الأساسية،
/// لا أثناءها:
/// ═══════════════════════════════════════════════════════════════════
/// هذا الميثود بيحفظ حاله (SaveChangesAsync خاصة فيه) وبيرسل فعليًا عبر
/// تلغرام لو مفعّل — يعني استدعاؤه من *داخل* معاملة عملية بيع/تقفيل
/// صندوق (قبل ما تلتزم) بيخلق خطر حقيقي: رسالة تلغرام حقيقية تنبعث عن
/// شي لسه ممكن ينعكس (rollback) بعدين. يُستدعى فقط بعد ما
/// ITransactionalExecutor.ExecuteAsync يرجع نجاح، لا من جوّا الـdelegate
/// نفسها.
/// ═══════════════════════════════════════════════════════════════════
///
/// TargetUserId = null دايمًا هون — كل تنبيهات هذا النظام حاليًا "عامة"
/// (بلا مستقبل بشري محدد)، لأنه ما في نظام صلاحيات/أدوار فعلي يحدد "مين
/// المدير" (راجع تعليق Notification.TargetUserId بالـDomain). لما توجد
/// مصادقة حقيقية (D11)، هذا الصف بالذات هو اللي رح يتوسّع لدعم استهداف
/// مستخدم/دور محدد.
/// </summary>
public sealed class NotificationDispatcher : INotificationDispatcher
{
    private readonly IApplicationDbContext _context;
    private readonly IEnumerable<INotificationSender> _senders;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<NotificationDispatcher> _logger;

    public NotificationDispatcher(
        IApplicationDbContext context,
        IEnumerable<INotificationSender> senders,
        IDateTimeProvider dateTimeProvider,
        ILogger<NotificationDispatcher> logger)
    {
        _context = context;
        _senders = senders;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
    }

    public async Task NotifyAsync(string title, string message, CancellationToken cancellationToken)
    {
        try
        {
            // سجل "داخل النظام" — دايمًا يُنشأ، بغض النظر عن نجاح أي قناة
            // خارجية. هذا هو اللي endpoint الـpolling بيقرأ منه.
            _context.Notifications.Add(new Notification(targetUserId: null, title, message, NotificationChannel.InApp));

            // أفضل جهد لكل قناة خارجية مسجَّلة (تلغرام حاليًا). كل قناة
            // بتاخد سجل Notification منفصل خاص فيها — عشان محاولات الإرسال
            // والحالة (نجح/فشل) تنعرض بدقة لكل قناة لحالها.
            foreach (var sender in _senders)
            {
                var channelNotification = new Notification(targetUserId: null, title, message, sender.Channel);
                _context.Notifications.Add(channelNotification);

                bool success;
                string? errorMessage;

                try
                {
                    (success, errorMessage) = await sender.SendAsync(title, message, cancellationToken);
                }
                catch (Exception ex)
                {
                    // فشل الاتصال (شبكة، انتهاء مهلة) لا يستحق ما يوصل
                    // للمستدعي — بس هادئ ويُسجَّل كمحاولة فاشلة.
                    success = false;
                    errorMessage = ex.Message;
                }

                channelNotification.RecordDeliveryAttempt(_dateTimeProvider.UtcNow, success, errorMessage);
            }

            // حفظ خاص بهذا الميثود — مستقل عن أي معاملة استدعته (لازم
            // تكون هي أصلًا التزمت قبل ما نوصل هون، حسب التحذير بالأعلى).
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // فشل تسجيل الإشعار نفسه ما لازم يفشّل العملية اللي استدعته —
            // هي أصلًا خلصت والتزمت. نسجّل باللوج ونكمل.
            _logger.LogError(ex, "فشل تسجيل/إرسال إشعار: {Title}", title);
        }
    }
}
