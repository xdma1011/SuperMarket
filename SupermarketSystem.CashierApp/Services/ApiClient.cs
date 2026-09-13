using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using SupermarketSystem.CashierApp.Local;

namespace SupermarketSystem.CashierApp.Services;

/// <summary>DTOs مطابقة حرفيًا لشكل الرد الفعلي من الباك إند (راجع GetCatalogSyncPageQuery.cs وGetCatalogVersionQuery.cs).</summary>
public sealed record CatalogVersionDto(long Version);

public sealed record CatalogSyncUnitDto(
    Guid UnitId, string UnitName, decimal ConversionFactorToBase, bool IsBaseUnit, List<string> Barcodes);

public sealed record CatalogSyncBatchDto(
    Guid BatchId, string BatchNumber, DateOnly? ExpiryDate, decimal QuantityAvailable);

public sealed record CatalogSyncProductDto(
    Guid ProductId, string Name, Guid CategoryId, string CategoryName,
    decimal SellingPrice, bool IsAvailableForSale, bool IsBatchTracked,
    List<CatalogSyncUnitDto> Units, List<CatalogSyncBatchDto> Batches);

public sealed record PagedResultDto<T>(List<T> Items, int TotalCount, int PageNumber, int PageSize);

public sealed record StoreBrandingDto(string? StoreName);

public sealed record PaymentSettingsDto(decimal UsdToJodExchangeRate);

/// <summary>مطابق حرفيًا لـReportDiscardedPendingSaleCommand بالباك إند.</summary>
public sealed record ReportDiscardedPendingSaleRequestDto(
    Guid ClientRequestId, Guid BranchId, DateTime CreatedAtLocal, int AttemptCount, string? LastErrorMessage);

public sealed record LogoutRequestDto(string RefreshToken);

/// <summary>مطابق حرفيًا لـCompleteCashClosingCommand بالباك إند.</summary>
public sealed record CompleteCashClosingCountedDetailDto(Guid PaymentMethodId, decimal CountedAmount);

public sealed record CompleteCashClosingRequestDto(
    Guid BranchId, DateOnly BusinessDate, decimal CountedCash, List<CompleteCashClosingCountedDetailDto> CountedDetails);

/// <summary>بس الحقول اللي شاشة الكاشير فعليًا بتعرضها - التفاصيل حسب طريقة الدفع (Details) موجودة بالرد الفعلي بس غير مستخدَمة هون.</summary>
public sealed record CompleteCashClosingResponseDto(Guid CashClosingId, decimal ExpectedCash, decimal CountedCash, decimal Variance);

public sealed record CashClosingResult(bool Success, CompleteCashClosingResponseDto? Response, string? ErrorMessage);

/// <summary>يطابق ClientAppType بالباك إند حرفيًا (Cashier = 1, Admin = 2) - قيمة الـenum لازم تبقى مطابقة، لأنها بتُسلسَل كرقم بالـJSON.</summary>
public enum ClientAppType
{
    Cashier = 1,
    Admin = 2
}

/// <summary>مطابق حرفيًا لـLoginCommand بالباك إند.</summary>
public sealed record LoginRequestDto(
    string Username, string Password, ClientAppType AppType, Guid? BranchId, string? IpAddress, string? DeviceInfo);

/// <summary>مطابق حرفيًا لـLoginResponse بالباك إند.</summary>
public sealed record LoginResponseDto(
    string AccessToken, DateTime AccessTokenExpiresAtUtc, string RefreshToken, DateTime RefreshTokenExpiresAtUtc,
    Guid UserId, string FullName, Guid? BranchId, bool PreviousSessionRevoked);

public sealed record LoginResult(bool Success, LoginResponseDto? Response, string? ErrorMessage);

/// <summary>مطابق حرفيًا لـPaymentMethodDto بالباك إند.</summary>
public sealed record PaymentMethodDto(Guid Id, string Name, bool RequiresExternalReference);

/// <summary>
/// أبسط عميل ممكن — ميثودان أصليان (SendPendingSaleAsync) + ميثودا
/// مزامنة الكتالوج المضافتان هون.
/// </summary>
public sealed class ApiClient
{
    private readonly HttpClient _http;

    public ApiClient(AppConfig config)
    {
        _http = new HttpClient { BaseAddress = new Uri(config.ApiBaseUrl.TrimEnd('/') + "/") };
        if (!string.IsNullOrWhiteSpace(config.AccessToken))
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config.AccessToken);
        }
    }

    /// <summary>يُستدعى بعد نجاح تسجيل الدخول - كل طلب بعد هيك بيحمل التوكن تلقائيًا.</summary>
    public void SetAccessToken(string accessToken)
    {
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }

    /// <summary>
    /// POST /auth/login مباشرة — بلا صلاحية مسبقة، هذا الـendpoint اللي
    /// يعطي التوكن نفسه. لا Idempotency هون (تسجيل الدخول عملية طبيعية
    /// تتكرر)، بخلاف عمليات البيع.
    /// </summary>
    public async Task<LoginResult> LoginAsync(string username, string password, CancellationToken cancellationToken)
    {
        try
        {
            var request = new LoginRequestDto(username, password, ClientAppType.Cashier, BranchId: null, IpAddress: null, DeviceInfo: Environment.MachineName);
            var response = await _http.PostAsJsonAsync("auth/login", request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadFromJsonAsync<LoginResponseDto>(cancellationToken: cancellationToken);
                return body is null
                    ? new LoginResult(false, null, "رد غير متوقَّع من السيرفر.")
                    : new LoginResult(true, body, null);
            }

            // رسالة فشل واحدة عامة بقصد (راجع تعليق LoginResponse بالباك إند:
            // "مبدأ حاكم: رسالة فشل واحدة لكل الأسباب") - لا نميّز هون بين
            // خطأ اسم مستخدم أو كلمة سر، نفس فلسفة الباك إند بالضبط.
            return new LoginResult(false, null, "اسم المستخدم أو كلمة السر غير صحيحة.");
        }
        catch (Exception ex)
        {
            return new LoginResult(false, null, $"تعذّر الاتصال بالسيرفر: {ex.Message}");
        }
    }

    /// <summary>
    /// خروج طوعي - POST /auth/logout (AllowAnonymous بالباك إند، بلا
    /// حاجة توكن). أفضل-محاولة عمدًا: فشلها (نت مقطوع مثلًا) ما يمنع
    /// الخروج المحلي - الجلسة بالذاكرة بتُمسح بكل الأحوال من الطرف
    /// اللي بينادي هالميثود (LoginWindow جديدة = جلسة نظيفة تلقائيًا).
    /// </summary>
    public async Task<bool> LogoutAsync(string refreshToken, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("auth/logout", new LogoutRequestDto(refreshToken), cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// POST /cash-closings - يحتاج صلاحية CashClosing.Manage (كاشير عادي
    /// ما عنده هذه الصلاحية افتراضيًا، مقصود - الشاشة اللي بتنادي هالميثود
    /// محمية بباسوورد الأدمن المحلي، راجع CashClosingWindow).
    /// </summary>
    public async Task<CashClosingResult> CompleteCashClosingAsync(
        Guid branchId, DateOnly businessDate, decimal countedCash,
        List<CompleteCashClosingCountedDetailDto> countedDetails, CancellationToken cancellationToken)
    {
        try
        {
            var request = new CompleteCashClosingRequestDto(branchId, businessDate, countedCash, countedDetails);
            var response = await _http.PostAsJsonAsync("cash-closings", request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadFromJsonAsync<CompleteCashClosingResponseDto>(cancellationToken: cancellationToken);
                return body is null
                    ? new CashClosingResult(false, null, "رد غير متوقَّع من السيرفر.")
                    : new CashClosingResult(true, body, null);
            }

            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            return new CashClosingResult(false, null, $"{(int)response.StatusCode}: {errorBody}");
        }
        catch (Exception ex)
        {
            return new CashClosingResult(false, null, $"تعذّر الاتصال بالسيرفر: {ex.Message}");
        }
    }

    /// <summary>طرق الدفع نادرًا ما تتغيّر - تُجلب مرة بالذاكرة بعد الدخول، بلا حاجة لآلية مزامنة كاملة.</summary>
    public async Task<List<PaymentMethodDto>> GetPaymentMethodsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _http.GetFromJsonAsync<List<PaymentMethodDto>>("payment-methods", cancellationToken);
            return result ?? new List<PaymentMethodDto>();
        }
        catch
        {
            return new List<PaymentMethodDto>();
        }
    }

    /// <summary>استعلام خفيف جدًا — يُستدعى بشكل متكرر (كل SyncIntervalSeconds) ليقرر هل يحتاج مزامنة فعلية.</summary>
    public async Task<long?> GetCatalogVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _http.GetFromJsonAsync<CatalogVersionDto>("cashier-sync/catalog-version", cancellationToken);
            return result?.Version;
        }
        catch
        {
            // فشل الاتصال (نت مقطوع) - يرجع null، الطالب (CatalogSyncService)
            // بيتعامل معها كـ"تخطَّ هالدورة"، لا خطأ يوقف التطبيق.
            return null;
        }
    }

    public async Task<StoreBrandingDto?> GetStoreBrandingAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _http.GetFromJsonAsync<StoreBrandingDto>("cashier-sync/store-branding", cancellationToken);
        }
        catch
        {
            // بلا اتصال - الطالب بيرجع للنسخة المخزَّنة محليًا (راجع StoreBrandingCache).
            return null;
        }
    }

    public async Task<PaymentSettingsDto?> GetPaymentSettingsAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _http.GetFromJsonAsync<PaymentSettingsDto>("cashier-sync/payment-settings", cancellationToken);
        }
        catch
        {
            // بلا اتصال - الطالب بيرجع للنسخة المخزَّنة محليًا (راجع PaymentSettingsCache).
            return null;
        }
    }

    public async Task<PagedResultDto<CatalogSyncProductDto>?> GetCatalogSyncPageAsync(
        Guid branchId, int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        try
        {
            var url = $"cashier-sync/catalog-page?branchId={branchId}&pageNumber={pageNumber}&pageSize={pageSize}";
            return await _http.GetFromJsonAsync<PagedResultDto<CatalogSyncProductDto>>(url, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// يبعت جسم البيع المخزَّن محليًا كما هو (JSON خام) لـPOST /sales.
    /// نفس ClientRequestId المخزَّن أصلًا بالـPayload — لو الطلب انبعت
    /// قبل ونجح فعليًا بس الرد ضاع، السيرفر بيرجّع WasReplay=true بدل
    /// ما يسجّل بيع مكرَّر.
    /// </summary>
    /// <summary>
    /// IsConnectivityFailure يفرّق بين حالتين مختلفتين تمامًا لازم يتعامل
    /// معهم PendingSaleSyncService بشكل مختلف:
    ///   - استثناء (نت مقطوع كليًا، السيرفر مو راد أصلًا) → true. باقي
    ///     الطابور غالبًا رح يفشل لنفس السبب، فمعنى تكمل تجرّب كل وحدة
    ///     صفر - وقف الدفعة كاملة أذكى.
    ///   - رد فعلي من السيرفر برفض (400/401/409...) → false. هاي مشكلة
    ///     خاصة بهاي الفاتورة بالذات (منتج انحذف، خطأ مصادقة، تعارض) -
    ///     باقي الطابور ممكن يكون سليم تمامًا، ما في مبرر يتعطّل بسببها.
    /// </summary>
    public async Task<PendingSaleSendResult> SendPendingSaleAsync(PendingSale pendingSale, CancellationToken cancellationToken)
    {
        try
        {
            var content = new StringContent(pendingSale.RequestPayloadJson, System.Text.Encoding.UTF8, "application/json");
            var response = await _http.PostAsync("sales", content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return new PendingSaleSendResult(Success: true, ErrorMessage: null, IsConnectivityFailure: false);
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return new PendingSaleSendResult(Success: false, ErrorMessage: $"{(int)response.StatusCode}: {body}", IsConnectivityFailure: false);
        }
        catch (Exception ex)
        {
            return new PendingSaleSendResult(Success: false, ErrorMessage: ex.Message, IsConnectivityFailure: true);
        }
    }

    /// <summary>
    /// POST /cashier-sync/report-discarded-pending-sale — أفضل-محاولة صريحة
    /// (راجع PendingQueueWindow.DiscardButton_Click): تُستدعى *قبل* الحذف
    /// المحلي، بس فشلها (أوفلاين فعليًا) ما يمنع الحذف المحلي من الصير -
    /// هذا الميثود بيرجّع bool فقط، بلا استثناء يوصل للمستدعي إطلاقًا.
    /// </summary>
    public async Task<bool> ReportDiscardedPendingSaleAsync(PendingSale pendingSale, CancellationToken cancellationToken)
    {
        try
        {
            var request = new ReportDiscardedPendingSaleRequestDto(
                pendingSale.ClientRequestId, pendingSale.BranchId, pendingSale.CreatedAtLocal,
                pendingSale.AttemptCount, pendingSale.LastErrorMessage);

            var response = await _http.PostAsJsonAsync("cashier-sync/report-discarded-pending-sale", request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            // بلا اتصال أو رفض من السيرفر - الحذف المحلي بيصير بكل الأحوال
            // (راجع PendingQueueWindow)، هذا مجرد أفضل-محاولة للإشعار.
            return false;
        }
    }

    /// <summary>
    /// فحص اتصال فعلي بالسيرفر — GET /health (بلا توكن، مفعّلة AllowAnonymous
    /// بالباك إند). مهلة قصيرة عمدًا (3 ثواني): هدف هالفحص تفعيل/تعطيل زر
    /// "مزامنة الآن" بالواجهة، مو انتظار طويل. NetworkInterface.GetIsNetworkAvailable
    /// كان بيتحقق بس من وجود كرت شبكة فعّال، لا اتصال فعلي بالسيرفر - هيك
    /// أدق: بيتأكد فعلًا إنه ممكن نوصل للباك إند.
    /// </summary>
    public async Task<bool> IsServerReachableAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            // مسار مطلق من جذر الدومين (/health) عمدًا - لا health/ نسبي:
            // _http.BaseAddress منتهٍ بـ/api/v1/، بينما endpoint الصحة
            // بالباك إند مسجَّل على /health مباشرة (بلا بادئة api/v1، راجع
            // Program.cs) - نسبي كان بيطلب /api/v1/health (404) دائمًا،
            // ويعرض "بلا اتصال" حتى لو السيرفر شغّال فعليًا.
            var response = await _http.GetAsync("/health", linkedCts.Token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// POST /purchase-invoices/drafts/from-image — يحتاج صلاحية
    /// Purchasing.CreateDraft بس (موجودة افتراضيًا لدور الكاشير بالباك
    /// إند)، لا Purchasing.Create. الرد نفسه (مسودة كاملة) ما بيهمّنا هون
    /// - الكاشير ما بيراجع، بس بيتأكد النجاح أو الفشل.
    /// </summary>
    public async Task<UploadInvoiceDraftResult> UploadPurchaseInvoiceDraftAsync(
        Guid branchId, byte[] imageBytes, string fileName, string contentType,
        decimal? paidNowAmount, Guid? paidNowPaymentMethodId, CancellationToken cancellationToken)
    {
        try
        {
            using var content = new MultipartFormDataContent();
            using var fileContent = new ByteArrayContent(imageBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(contentType) ? "image/jpeg" : contentType);
            content.Add(fileContent, "file", fileName);

            var query = $"purchase-invoices/drafts/from-image?branchId={branchId}";
            if (paidNowAmount is { } amount && paidNowPaymentMethodId is { } methodId)
            {
                query += $"&paidNowAmount={amount.ToString(System.Globalization.CultureInfo.InvariantCulture)}&paidNowPaymentMethodId={methodId}";
            }

            var response = await _http.PostAsync(query, content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return new UploadInvoiceDraftResult(true, null);
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return new UploadInvoiceDraftResult(false, $"{(int)response.StatusCode}: {body}");
        }
        catch (Exception ex)
        {
            return new UploadInvoiceDraftResult(false, $"تعذّر الاتصال بالسيرفر: {ex.Message}");
        }
    }
}

public sealed record PendingSaleSendResult(bool Success, string? ErrorMessage, bool IsConnectivityFailure);
public sealed record UploadInvoiceDraftResult(bool Success, string? ErrorMessage);
