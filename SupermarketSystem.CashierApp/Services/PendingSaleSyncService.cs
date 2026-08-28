using Microsoft.EntityFrameworkCore;
using SupermarketSystem.CashierApp.Local;

namespace SupermarketSystem.CashierApp.Services;

/// <summary>
/// "إرسال المبيعات المعلَّقة" — الجزء المتمم لطابور PendingSale. تُستدعى
/// دوريًا (نفس فترة SyncIntervalSeconds بالإعدادات) أو يدويًا بعد إتمام
/// بيع جديد. المنطق مقصود بسيط: خذ المعلَّقات بالترتيب، جرّب ابعتها،
/// احذف الناجحة، سجّل خطأ الفاشلة واتركها لمحاولة لاحقة — بلا أي حذف
/// أو تعديل على المبيعات نفسها، تفاديًا لأي فقدان بيانات.
///
/// ترتيب الإرسال بالتسلسل (لا بالتوازي) عمدًا — لو أول عملية بالطابور
/// فشلت بخطأ حقيقي (لا مجرد انقطاع نت)، نوقف الدفعة كاملة ونكمل
/// بالمحاولة الجاية، بدل ما نبعت بقية الطابور بلا داعٍ.
/// </summary>
public sealed class PendingSaleSyncService
{
    private readonly string _dbPath;
    private readonly ApiClient _apiClient;

    public PendingSaleSyncService(string dbPath, ApiClient apiClient)
    {
        _dbPath = dbPath;
        _apiClient = apiClient;
    }

    public async Task<SyncSummary> SyncPendingSalesAsync(CancellationToken cancellationToken)
    {
        using var db = new LocalDbContext(_dbPath);

        var pendingSales = await db.PendingSales
            .OrderBy(s => s.CreatedAtLocal)
            .ToListAsync(cancellationToken);

        var sentCount = 0;
        var failedCount = 0;

        foreach (var sale in pendingSales)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var result = await _apiClient.SendPendingSaleAsync(sale, cancellationToken);

            if (result.Success)
            {
                // نجح فعليًا (أو WasReplay=true من السيرفر لبيع سبق ونجح) —
                // بالحالتين البيع موجود بالسيرفر الآن، نحذف النسخة المحلية.
                db.PendingSales.Remove(sale);
                await db.SaveChangesAsync(cancellationToken);
                sentCount++;
            }
            else
            {
                sale.AttemptCount++;
                sale.LastAttemptAtLocal = DateTime.UtcNow;
                sale.LastErrorMessage = result.ErrorMessage;
                await db.SaveChangesAsync(cancellationToken);
                failedCount++;
                break;
            }
        }

        return new SyncSummary(sentCount, failedCount, pendingSales.Count - sentCount - failedCount);
    }
}

public sealed record SyncSummary(int Sent, int Failed, int StillPending);
