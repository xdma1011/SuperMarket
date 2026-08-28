namespace SupermarketSystem.CashierApp.Services;

/// <summary>
/// حالة الجلسة الحالية بالذاكرة فقط (بلا حفظ على القرص عمدًا — نفس
/// مبدأ الفرونت إند بالويب: التوكن بالذاكرة، لا حفظ دائم، تفاديًا
/// لبقاء توكن صالح على جهاز مسروق أو مُشترَك). لو التطبيق طُفي، لازم
/// تسجيل دخول جديد.
/// </summary>
public sealed class AuthSession
{
    public string? AccessToken { get; private set; }
    public DateTime? AccessTokenExpiresAtUtc { get; private set; }
    public string? RefreshToken { get; private set; }
    public Guid? UserId { get; private set; }
    public string? FullName { get; private set; }
    public Guid? BranchId { get; private set; }

    public bool IsLoggedIn => AccessToken is not null;

    public void SetSession(LoginResponseDto response)
    {
        AccessToken = response.AccessToken;
        AccessTokenExpiresAtUtc = response.AccessTokenExpiresAtUtc;
        RefreshToken = response.RefreshToken;
        UserId = response.UserId;
        FullName = response.FullName;
        BranchId = response.BranchId;
    }

    public void Clear()
    {
        AccessToken = null;
        AccessTokenExpiresAtUtc = null;
        RefreshToken = null;
        UserId = null;
        FullName = null;
        BranchId = null;
    }
}
