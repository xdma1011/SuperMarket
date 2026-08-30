using System.Windows.Threading;

namespace SupermarketSystem.CashierApp.Services;

/// <summary>
/// هون الجزء اللي بيخلّي كل شي بنيناه (CatalogSyncService،
/// PendingSaleSyncService) يشتغل فعليًا لا نظريًا بس. بتبدأ فور نجاح
/// تسجيل الدخول، وتضل شغّالة لعمر التطبيق كامل — بلا ربط بنافذة
/// معيّنة (الشاشات بتفتح وتسكر، بس المزامنة لازم تضل مستمرة).
///
/// DispatcherTimer مُختار عمدًا (لا Timer عادي) — بيشتغل على UI Thread
/// نفسه، فما في حاجة لأي Marshalling لو حبينا لاحقًا نحدّث شي بالواجهة
/// من نفس الـTick مباشرة.
///
/// _isRunning علم بسيط يمنع Tick جديد يبلش قبل ما يخلص Tick سابق —
/// لو المزامنة أخدت وقت أطول من الفترة المحدَّدة، ما نراكم عمليات
/// متوازية فوق بعض.
/// </summary>
public sealed class BackgroundSyncService
{
    private readonly ApiClient _apiClient;
    private readonly string _dbPath;
    private readonly int _catalogPageSize;
    private readonly DispatcherTimer _timer;

    private bool _isRunning;
    private Guid? _branchId;
    private CancellationTokenSource? _manualSyncCts;

    public DateTime? LastSuccessfulSyncAtLocal { get; private set; }
    public string? LastErrorMessage { get; private set; }

    /// <summary>Tick التلقائي وضغطة "مزامنة الآن" اليدوية بيشتركوا بنفس هالعلم - مزامنتان متوازيتان بأي وقت ممنوعتان.</summary>
    public bool IsSyncing => _isRunning;

    public BackgroundSyncService(ApiClient apiClient, string dbPath, int syncIntervalSeconds, int catalogPageSize)
    {
        _apiClient = apiClient;
        _dbPath = dbPath;
        _catalogPageSize = catalogPageSize;

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Math.Max(10, syncIntervalSeconds))
        };
        _timer.Tick += async (_, _) => await RunOneCycleAsync();
    }

    /// <summary>يُستدعى مرة وحدة فور نجاح تسجيل الدخول.</summary>
    public void Start(Guid branchId)
    {
        _branchId = branchId;
        _timer.Start();

        // أول دورة فورية (بلا انتظار الفترة الكاملة) - لو المستخدم
        // دخل بعد فترة أوفلاين طويلة، ما في داعي ينتظر دقيقة كاملة
        // قبل أول محاولة مزامنة.
        _ = RunOneCycleAsync();
    }

    public void Stop()
    {
        _timer.Stop();
    }

    private async Task RunOneCycleAsync() => await RunOneCycleAsync(CancellationToken.None);

    /// <summary>
    /// نتيجة دورة مزامنة يدوية واحدة - تفرّق بين "خلصت بنجاح"، "انلغت
    /// بضغطة المستخدم"، و"فشلت" (LastErrorMessage فيه التفصيل بالحالة
    /// الأخيرة). الواجهة (SaleWindow) بتعرض رسالة مختلفة حسب كل حالة.
    /// </summary>
    public enum ManualSyncOutcome { Completed, Cancelled, AlreadyRunning, Failed }

    /// <summary>
    /// كبسة "مزامنة الآن" - نفس منطق الدورة التلقائية بالضبط (Tick)، بس
    /// بـCancellationToken فعلي يقدر المستخدم يلغيه لو أخد وقت أطول من
    /// المتوقع (مثلًا نت بطيء جدًا لا مقطوع كليًا). لا تشتغل لو فيه دورة
    /// تلقائية أو يدوية شغّالة أصلًا - _isRunning علم مشترك بين الاثنين.
    /// </summary>
    public async Task<ManualSyncOutcome> TriggerManualSyncAsync()
    {
        if (_isRunning || _branchId is null)
        {
            return ManualSyncOutcome.AlreadyRunning;
        }

        var cts = new CancellationTokenSource();
        _manualSyncCts = cts;

        try
        {
            await RunOneCycleAsync(cts.Token);

            // ApiClient بيبلع كل استثناء (حتى OperationCanceledException) ويرجّع
            // null/فشل هادئ - نفس فلسفة "لا نفجّر لأجل نت مقطوع" (راجع تعليقات
            // ApiClient.GetCatalogSyncPageAsync). يعني الإلغاء هون ما بيوصل
            // كاستثناء لهون غالبًا - لازم نفحص IsCancellationRequested صراحة
            // بعد رجوع الاستدعاء العادي، لا نعتمد بس على catch تحت.
            if (cts.IsCancellationRequested)
            {
                LastErrorMessage = "أُلغيت المزامنة بطلب المستخدم.";
                return ManualSyncOutcome.Cancelled;
            }

            return LastErrorMessage is null ? ManualSyncOutcome.Completed : ManualSyncOutcome.Failed;
        }
        catch (OperationCanceledException)
        {
            LastErrorMessage = "أُلغيت المزامنة بطلب المستخدم.";
            return ManualSyncOutcome.Cancelled;
        }
        finally
        {
            cts.Dispose();
            _manualSyncCts = null;
        }
    }

    /// <summary>يُستدعى من زر "إلغاء" بالواجهة - بلا تأثير لو ما في مزامنة يدوية شغّالة أصلًا (Tick التلقائي غير قابل للإلغاء عمدًا، هو خفيف وسريع أصلًا).</summary>
    public void CancelManualSync()
    {
        _manualSyncCts?.Cancel();
    }

    private async Task RunOneCycleAsync(CancellationToken cancellationToken)
    {
        if (_isRunning || _branchId is null)
        {
            return;
        }

        _isRunning = true;

        try
        {
            var pendingSaleSync = new PendingSaleSyncService(_dbPath, _apiClient);
            await pendingSaleSync.SyncPendingSalesAsync(cancellationToken);

            await RefreshPaymentMethodsAsync(cancellationToken);

            var catalogSync = new CatalogSyncService(_dbPath, _apiClient, _catalogPageSize);
            var result = await catalogSync.SyncIfNeededAsync(_branchId.Value, cancellationToken);

            if (result.Status is CatalogSyncStatus.Completed or CatalogSyncStatus.AlreadyUpToDate)
            {
                LastSuccessfulSyncAtLocal = DateTime.UtcNow;
                LastErrorMessage = null;
            }
            else
            {
                LastErrorMessage = "تعذّر إكمال مزامنة الكتالوج - رح تُعاد المحاولة بالدورة الجاية.";
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LastErrorMessage = ex.Message;
        }
        finally
        {
            _isRunning = false;
        }
    }

    /// <summary>
    /// نادرًا ما تتغيّر — استبدال كامل بسيط بدل مقارنة تفصيلية (نفس
    /// فلسفة CatalogSyncService.ApplyPageToLocalDb: أبسط من تعديل جزئي،
    /// وصحيح دائمًا). لو فشل الاتصال، الجدول المحلي القديم يضل كما هو،
    /// لا يُمسح — تخريب جزئي (مسح بلا استبدال) أسوأ من بيانات قديمة شوي.
    /// </summary>
    private async Task RefreshPaymentMethodsAsync(CancellationToken cancellationToken)
    {
        var methods = await _apiClient.GetPaymentMethodsAsync(cancellationToken);
        if (methods.Count == 0)
        {
            return;
        }

        using var db = new Local.LocalDbContext(_dbPath);
        db.PaymentMethods.RemoveRange(db.PaymentMethods);
        foreach (var method in methods)
        {
            db.PaymentMethods.Add(new Local.LocalPaymentMethod
            {
                Id = method.Id,
                Name = method.Name,
                RequiresExternalReference = method.RequiresExternalReference
            });
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}
