namespace SupermarketSystem.Application.Common.Interfaces;

public sealed record CustomerAccessTokenResult(string Token, DateTime ExpiresAtUtc);

/// <summary>
/// توكن هوية الزبون بعد نجاح تحقق OTP - منفصل تمامًا عن ITokenService
/// (مصادقة الموظفين/الكاشير)، عمدًا: الزبون لا يملك سجل User بجدول
/// الهوية الإدارية، ولا صلاحيات RBAC، ولا جلسة UserSession قابلة
/// للإبطال الفوري - فقط إثبات "هذا الطلب من صاحب رقم الهاتف X فعلًا"
/// بعد التحقق عبر تلغرام.
///
/// ⚠️ تنبيه صريح: الخطوة التالية غير المنفَّذة بعد هي ربط هذا التوكن
/// فعليًا بـPlaceOrder/GetCustomerOrders/FileComplaint/RateOrder (حاليًا
/// AllowAnonymous بمعامل CustomerPhone/CustomerId موثوق بلا تحقق - راجع
/// تعليقات ⚠️ بتلك الـendpoints). هذا الملف يبني آلية إصدار/تحقق التوكن
/// فقط، لا يُفعّلها بعد بنقاط النهاية القائمة.
/// </summary>
public interface ICustomerAuthTokenService
{
    CustomerAccessTokenResult CreateAccessToken(Guid customerId, string phone);

    /// <summary>يرجّع (CustomerId, Phone) لو التوكن صالح وموقَّع بشكل صحيح وغير منتهٍ، وإلا null.</summary>
    (Guid CustomerId, string Phone)? ValidateAccessToken(string token);
}
