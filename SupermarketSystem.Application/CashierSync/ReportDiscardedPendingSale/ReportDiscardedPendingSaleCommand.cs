using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;

namespace SupermarketSystem.Application.CashierSync.ReportDiscardedPendingSale;

/// <summary>
/// نفس الحقول المخزَّنة فعليًا بـPendingSale المحلي بتطبيق الكاشير
/// (SupermarketSystem.CashierApp/Local/LocalEntities.cs) - عمدًا بلا
/// UnitPrice/RequestPayloadJson، هذا الـcommand ما بينشئ أي بيع، بس
/// بيبلّغ إنه فاتورة معلَّقة انحذفت محليًا نهائيًا بلا ما توصل السيرفر.
/// </summary>
public sealed record ReportDiscardedPendingSaleCommand(
    Guid ClientRequestId,
    Guid BranchId,
    DateTime CreatedAtLocal,
    int AttemptCount,
    string? LastErrorMessage);

public sealed record ReportDiscardedPendingSaleResponse(bool Acknowledged);

/// <summary>
/// إشعار فقط - بلا أي أثر بقاعدة البيانات الحقيقية غير سجل الإشعار نفسه
/// (عبر INotificationDispatcher، وهذا سلوكه الطبيعي المتوقَّع لأي تنبيه
/// بالنظام). البيع المحذوف من طابور الكاشير المحلي *لم يصل السيرفر
/// إطلاقًا* - لو كان وصل، كان انسجل ببيع حقيقي وما كان ضل بالطابور
/// المحلي أصلًا - فما في داعٍ ولا معنى ننشئ سجل SaleInvoice أو أي كيان
/// بزنس له؛ هذا كان يكون تزوير لعملية ما صارت فعليًا.
///
/// الهدف الوحيد: يوصل صوت للمدير إنه فاتورة أوفلاين انحذفت نهائيًا بلا
/// ما توصل السيرفر - عشان يراجعها يدويًا (كاش/بضاعة اتحرّكوا فعليًا
/// بالمحل، بس بلا أي أثر بالنظام بعد الحذف).
///
/// أفضل-محاولة بالتصميم من طرف الكاشير (راجع ApiClient.ReportDiscardedPendingSaleAsync
/// وPendingQueueWindow.DiscardButton_Click) - لو فشل الاتصال، الحذف
/// المحلي بيصير بكل الأحوال بلا هذا الإشعار؛ هذا مقبول لأنه أصلًا معناه
/// "بلا اتصال".
/// </summary>
public sealed class ReportDiscardedPendingSaleHandler
{
    private readonly IApplicationDbContext _context;
    private readonly INotificationDispatcher _notificationDispatcher;

    public ReportDiscardedPendingSaleHandler(
        IApplicationDbContext context,
        INotificationDispatcher notificationDispatcher)
    {
        _context = context;
        _notificationDispatcher = notificationDispatcher;
    }

    public async Task<ReportDiscardedPendingSaleResponse> HandleAsync(
        ReportDiscardedPendingSaleCommand command, CancellationToken cancellationToken)
    {
        var branchName = await _context.Branches.AsNoTracking()
            .Where(b => b.Id == command.BranchId)
            .Select(b => b.Name)
            .FirstOrDefaultAsync(cancellationToken)
            ?? $"فرع غير معروف ({command.BranchId})";

        var lastErrorText = string.IsNullOrWhiteSpace(command.LastErrorMessage)
            ? "بلا خطأ مسجَّل"
            : command.LastErrorMessage;

        await _notificationDispatcher.NotifyAsync(
            "حذف فاتورة كاشير أوفلاين نهائيًا",
            $"الفرع: {branchName}\n" +
            $"وقت الإنشاء المحلي: {command.CreatedAtLocal:yyyy-MM-dd HH:mm}\n" +
            $"عدد محاولات الإرسال: {command.AttemptCount}\n" +
            $"آخر خطأ: {lastErrorText}\n\n" +
            "هذه الفاتورة لم تصل السيرفر إطلاقًا ولن تصل بعد الآن - إذا كان في كاش أو بضاعة تحرّكوا فعليًا بالمحل، لازم مراجعة يدوية.",
            cancellationToken);

        return new ReportDiscardedPendingSaleResponse(true);
    }
}
