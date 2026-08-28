using SupermarketSystem.Application.Common.Results;

namespace SupermarketSystem.Application.Common.Interfaces;

public static class ImageStorageSettingsKeys
{
    /// <summary>مجلد حفظ صور فواتير الشراء، محلي على السيرفر (نفس نمط قرار تخزين النسخ الاحتياطية).</summary>
    public const string PurchaseInvoiceImagesDirectory = "Storage.PurchaseInvoiceImagesDirectory";
}

/// <summary>
/// يحوّل أي صورة مرفوعة (JPEG، PNG، إلخ) لصيغة WebP ويخزّنها — حسب
/// المتطلب الأصلي "صورة الفاتورة الأصلية WebP" (تخزين أخف بلا فقدان وضوح
/// محسوس). يرجّع مرجع/مسار الملف المخزَّن، جاهز يُستخدم كـURL لاحقًا مع
/// PurchaseInvoice.AddImage الموجودة أصلًا بالـDomain.
/// </summary>
public interface IImageStorageService
{
    Task<Result<string>> SaveAsWebPAsync(byte[] imageBytes, CancellationToken cancellationToken);
}
