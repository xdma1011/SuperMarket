namespace SupermarketSystem.Application.Sales.Common;

/// <summary>
/// رقم الزبون المدخل عند الكاشير (28/9/2026). الأرقام بالنظام مخزّنة بصيغ مختلفة (تطبيق الزبائن/تلغرام
/// بيبعتوا دولي "9627..."، الكاشير بيكتب "07...")، فالمطابقة على آخر 9 أرقام (رقم الموبايل الأردني بلا
/// الصفر أو 962).
/// </summary>
public static class SaleCustomerPhone
{
    public const int MinDigits = 7;
    public const int MaxDigits = 15;

    /// <summary>أرقام بس (بيشيل مسافات/شرطات/+)، أو null لو فاضي أو عدد الأرقام مش منطقي.</summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var digits = new string(raw.Where(char.IsAsciiDigit).ToArray());
        return digits.Length is >= MinDigits and <= MaxDigits ? digits : null;
    }

    public static string MatchKey(string normalizedPhone) =>
        normalizedPhone.Length <= 9 ? normalizedPhone : normalizedPhone[^9..];
}
