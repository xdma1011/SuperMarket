namespace SupermarketSystem.Application.Partners;

/// <summary>
/// طرق تحقق الشريك بالكاشير (29/9/2026، "خليها اختيارية من الضبط"): يوزر وكلمة سر الشريك دايمًا شغّالة؛ كود تلغرام
/// والباركود الشخصي كل واحد بيتفعّل لحاله من صفحة الإعدادات (افتراضيًا مطفيين).
/// </summary>
public static class PartnerVerificationSettingsKeys
{
    public const string TelegramOtpEnabled = "Partners.CashierVerify.TelegramOtpEnabled";
    public const string BarcodeEnabled = "Partners.CashierVerify.BarcodeEnabled";
}
