using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using SupermarketSystem.CashierApp.Local;

namespace SupermarketSystem.CashierApp.Services;

/// <summary>
/// الساعة الموثوقة للكاشير (بند 22، قرار صاحب المشروع 6/10/2026): بيعة أوفلاين لازم تنختم بالوقت الصح مش بساعة ويندوز
/// (ممكن تكون غلط أو انغيّرت)، لأنه وقت البيعة بيحدد شهر الربح والوردية والعروض الشغّالة.
///
/// المصدر الوحيد للوقت = السيرفر (مش خدمة وقت أونلاين: لو النت قاطع وهاد سبب المشكلة أصلًا). كل دورة مزامنة (~60 ثانية)
/// بتجيب وقت السيرفر وبتحفظه محليًا (TrustedTimeAnchor: وقت السيرفر + ساعة الجهاز لحظتها + حارس HighWater).
///
/// "الآن الموثوق" (Now):
///  1. جلسة التشغيل الحالية فيها مزامنة ناجحة: وقت السيرفر + الوقت المنقضي بساعة رتيبة (Stopwatch - ما بتتأثر بتغيير ساعة ويندوز).
///  2. بعد إعادة تشغيل بلا نت: ساعة الجهاز الحالية + الفرق المحفوظ (ServerUtc - DeviceUtcAtSync).
///  3. أول تشغيل بلا أي مزامنة سابقة: ساعة الجهاز (ما في أفضل منها) - والسيرفر بيعلّم الوقت المشبوه وقت وصول البيعة.
/// وفي كل الأحوال الناتج ما بينزل عن HighWaterUtc (آخر وقت موثوق انختم فيه إشي)، ومع كل مزامنة ناجحة HighWater بينعاد ضبطه
/// على وقت السيرفر (المرجع) - فحارس مسمَّم بساعة غلط ما بيضل للأبد.
///
/// حد صريح: لو الساعة اتغيّرت وهو بلا نت ثم انعاد تشغيل الجهاز، ما بنقدر نكشفها قبل أول مزامنة (بتنكشف عندها).
/// </summary>
public sealed class TrustedClock
{
    public static TrustedClock Instance { get; } = new();

    private readonly object _gate = new();

    // جلسة التشغيل الحالية.
    private DateTime? _sessionServerUtc;
    private long _sessionStopwatchTimestamp;

    // المحفوظ.
    private TimeSpan? _persistedOffset;
    private DateTime _highWaterUtc = DateTime.MinValue;

    /// <summary>true لو في أي مرجع من السيرفر (جلسة حالية أو محفوظ) - false = بنشتغل بساعة الجهاز الخام.</summary>
    public bool HasServerReference
    {
        get
        {
            lock (_gate)
            {
                return _sessionServerUtc is not null || _persistedOffset is not null;
            }
        }
    }

    /// <summary>تحميل آخر مرجع محفوظ (مرة عند فتح التطبيق، قبل أي بيع). فشل القراءة = نكمل بساعة الجهاز.</summary>
    public void Load(string dbPath)
    {
        try
        {
            using var db = new LocalDbContext(dbPath);
            var anchor = db.TrustedTimeAnchors.AsNoTracking().FirstOrDefault();
            if (anchor is null)
            {
                return;
            }

            lock (_gate)
            {
                _persistedOffset = anchor.ServerUtc - anchor.DeviceUtcAtSync;
                _highWaterUtc = DateTime.SpecifyKind(anchor.HighWaterUtc, DateTimeKind.Utc);
            }
        }
        catch (Exception)
        {
            // جدول مش جاهز / ملف تالف: الساعة الموثوقة اختيارية، ما بتوقف التطبيق.
        }
    }

    /// <summary>الوقت الموثوق الآن (UTC).</summary>
    public DateTime Now()
    {
        lock (_gate)
        {
            return ComputeNowLocked();
        }
    }

    /// <summary>الوقت الموثوق بتوقيت جهاز الكاشير المحلي (لعرض/تاريخ يوم) - نفس الأساس، بس تحويل منطقة.</summary>
    public DateTime LocalNow() => Now().ToLocalTime();

    private DateTime ComputeNowLocked()
    {
        DateTime candidate;
        if (_sessionServerUtc is { } sessionServer)
        {
            candidate = sessionServer + Stopwatch.GetElapsedTime(_sessionStopwatchTimestamp);
        }
        else if (_persistedOffset is { } offset)
        {
            candidate = DateTime.UtcNow + offset;
        }
        else
        {
            candidate = DateTime.UtcNow;
        }

        return candidate < _highWaterUtc ? _highWaterUtc : candidate;
    }

    /// <summary>
    /// يختم وقتًا موثوقًا ويرفع الحارس (بنفس SaveChanges تبع الـdb الممرَّر - يعني بنفس معاملة حفظ البيعة).
    /// لو ما في صف محفوظ بعد (أول تشغيل بلا مزامنة) بيتجاهل الحفظ - الحارس بالذاكرة بيحمي لحد ما يتحفظ أول مرجع.
    /// </summary>
    public DateTime Stamp(LocalDbContext db)
    {
        DateTime now;
        lock (_gate)
        {
            now = ComputeNowLocked();
            _highWaterUtc = now;
        }

        var anchor = db.TrustedTimeAnchors.FirstOrDefault();
        if (anchor is not null && anchor.HighWaterUtc < now)
        {
            anchor.HighWaterUtc = now;
        }

        return now;
    }

    /// <summary>
    /// مزامنة ناجحة مع السيرفر: serverUtcNow = ساعة السيرفر (مصحَّحة بنصف زمن الرحلة بالمُستدعي). بتتحفظ، وبتصير المرجع للجلسة،
    /// وبتعيد ضبط الحارس على وقت السيرفر (المرجع الأعلى).
    /// </summary>
    public async Task RecordServerTimeAsync(string dbPath, DateTime serverUtcNow, CancellationToken cancellationToken)
    {
        var deviceNow = DateTime.UtcNow;
        lock (_gate)
        {
            _sessionServerUtc = serverUtcNow;
            _sessionStopwatchTimestamp = Stopwatch.GetTimestamp();
            _persistedOffset = serverUtcNow - deviceNow;
            _highWaterUtc = serverUtcNow;
        }

        try
        {
            using var db = new LocalDbContext(dbPath);
            var anchor = await db.TrustedTimeAnchors.FirstOrDefaultAsync(cancellationToken);
            if (anchor is null)
            {
                db.TrustedTimeAnchors.Add(new TrustedTimeAnchor
                {
                    ServerUtc = serverUtcNow, DeviceUtcAtSync = deviceNow, HighWaterUtc = serverUtcNow
                });
            }
            else
            {
                anchor.ServerUtc = serverUtcNow;
                anchor.DeviceUtcAtSync = deviceNow;
                anchor.HighWaterUtc = serverUtcNow;
            }

            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception)
        {
            // الحفظ أفضل جهد - المرجع بالذاكرة شغّال لهالجلسة على الأقل.
        }
    }
}
