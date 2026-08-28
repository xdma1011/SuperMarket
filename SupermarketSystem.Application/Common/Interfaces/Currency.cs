namespace SupermarketSystem.Application.Common.Interfaces;

/// <summary>
/// قائمة العملات المدعومة — دينار أردني ودولار فقط مبدئيًا، حسب الطلب
/// الصريح. توسيعها لاحقًا يعني إضافة قيمة جديدة هون بس.
/// </summary>
public enum Currency
{
    JOD = 1,
    USD = 2
}

public sealed record CurrencyInfo(int Id, string Code, string Name);

public static class CurrencyCatalog
{
    /// <summary>مصدر الحقيقة الوحيد لأسماء العملات المعروضة.</summary>
    public static readonly IReadOnlyList<CurrencyInfo> All = new[]
    {
        new CurrencyInfo((int)Currency.JOD, "JOD", "دينار أردني"),
        new CurrencyInfo((int)Currency.USD, "USD", "دولار أمريكي")
    };
}

/// <summary>
/// قائمة الوحدات الشائعة — اقتراحات جاهزة للفرونت إند، لا قيد صارم.
/// ProductUnit.UnitName يضل نصًا حرًا بالباك إند عمدًا (منتج ممكن يحتاج
/// وحدة غير موجودة هون، زي "برميل" أو "لفة") — هذي القائمة بس تسهّل
/// الاختيار الشائع بدل كتابة كل شي يدويًا كل مرة.
/// </summary>
public static class CommonUnits
{
    public static readonly IReadOnlyList<string> All = new[]
    {
        "حبة", "كيلو", "غرام", "لتر", "مل",
        "كرتونة", "علبة", "صندوق", "كيس", "طرد",
        "دستة", "ربطة", "لفة", "زجاجة", "عبوة"
    };
}
