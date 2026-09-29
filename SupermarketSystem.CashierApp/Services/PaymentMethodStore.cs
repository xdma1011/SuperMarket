using SupermarketSystem.CashierApp.Local;

namespace SupermarketSystem.CashierApp.Services;

/// <summary>
/// طرق الدفع محليًا (29/9/2026، صاحب المشروع: "خليها تتخزن لوكالي... وبس يكون فيه نت يحدثها مرة في اليوم او مع زر
/// المزامنة"). شاشة البيع بتقرأ من هون دايمًا (بلا نت)، والسيرفر بينطلب بس: أول مرة (الجدول فاضي)، مرة كل 24 ساعة
/// بالمزامنة الخلفية، أو مع "مزامنة الآن". فشل الاتصال ما بيمسح المحفوظ أبدًا.
/// </summary>
public static class PaymentMethodStore
{
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(24);

    public static List<PaymentMethodDto> Load(string dbPath)
    {
        using var db = new LocalDbContext(dbPath);
        return db.PaymentMethods
            .OrderBy(m => m.Name)
            .Select(m => new PaymentMethodDto(m.Id, m.Name, m.RequiresExternalReference))
            .ToList();
    }

    public static bool NeedsRefresh(string dbPath, DateTime nowUtc)
    {
        using var db = new LocalDbContext(dbPath);
        if (!db.PaymentMethods.Any())
        {
            return true;
        }

        var last = db.SyncStates.Select(s => s.LastPaymentMethodsRefreshAtUtc).FirstOrDefault();
        return last is null || nowUtc - last.Value >= RefreshInterval;
    }

    /// <summary>استبدال كامل (نادرًا ما تتغيّر). قائمة فاضية = ما بنمسح المحفوظ.</summary>
    public static async Task SaveAsync(string dbPath, IReadOnlyList<PaymentMethodDto> methods, DateTime nowUtc, CancellationToken cancellationToken)
    {
        if (methods.Count == 0)
        {
            return;
        }

        using var db = new LocalDbContext(dbPath);
        db.PaymentMethods.RemoveRange(db.PaymentMethods);
        foreach (var method in methods)
        {
            db.PaymentMethods.Add(new LocalPaymentMethod
            {
                Id = method.Id,
                Name = method.Name,
                RequiresExternalReference = method.RequiresExternalReference
            });
        }

        var state = db.SyncStates.FirstOrDefault();
        if (state is null)
        {
            // صف الحالة بينعمل أصلًا بأول مزامنة كتالوج؛ هون بس لو طرق الدفع وصلت قبلها. -1 = الكتالوج لسه ما انسحب.
            db.SyncStates.Add(new SyncState { LastSyncedCatalogVersion = -1, LastPaymentMethodsRefreshAtUtc = nowUtc });
        }
        else
        {
            state.LastPaymentMethodsRefreshAtUtc = nowUtc;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
