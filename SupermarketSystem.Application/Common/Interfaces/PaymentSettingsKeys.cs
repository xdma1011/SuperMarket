namespace SupermarketSystem.Application.Common.Interfaces;

/// <summary>
/// كانت مفقودة - سعر تحويل الدولار للدينار كان مثبَّت بالرأس بلا أي طريقة
/// تعديله، بينما هو سعر يتغيّر فعليًا (حتى لو ببطء) وقرار إداري لا قيمة
/// ثابتة بالكود.
/// </summary>
public static class PaymentSettingsKeys
{
    /// <summary>كم دينار يساوي الدولار الواحد - الافتراضي .71 (سعر شبه ثابت تاريخيًا بالأردن).</summary>
    public const string UsdToJodExchangeRate = "Payment.UsdToJodExchangeRate";
}
