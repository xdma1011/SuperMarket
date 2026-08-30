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
    public async Task<PendingSaleSendResult> SendPendingSaleAsync(PendingSale pendingSale, CancellationToken cancellationToken)
    {
        try
        {
            var content = new StringContent(pendingSale.RequestPayloadJson, System.Text.Encoding.UTF8, "application/json");
            var response = await _http.PostAsync("sales", content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return new PendingSaleSendResult(Success: true, ErrorMessage: null);
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return new PendingSaleSendResult(Success: false, ErrorMessage: $"{(int)response.StatusCode}: {body}");
        }
        catch (Exception ex)
        {
            return new PendingSaleSendResult(Success: false, ErrorMessage: ex.Message);
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

            var response = await _http.GetAsync("health", linkedCts.Token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}

public sealed record PendingSaleSendResult(bool Success, string? ErrorMessage);
