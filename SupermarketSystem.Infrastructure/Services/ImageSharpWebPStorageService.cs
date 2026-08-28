using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;

namespace SupermarketSystem.Infrastructure.Services;

/// <summary>
/// SixLabors.ImageSharp — مكتبة مفتوحة المصدر، بلا تبعية على مكتبات نظام
/// تشغيل خارجية (بخلاف System.Drawing القديمة)، بتدعم قراءة أغلب صيغ
/// الصور الشائعة (JPEG، PNG، إلخ) وتشفير WebP أصليًا.
/// </summary>
public sealed class ImageSharpWebPStorageService : IImageStorageService
{
    private readonly ISettingsProvider _settingsProvider;

    public ImageSharpWebPStorageService(ISettingsProvider settingsProvider)
    {
        _settingsProvider = settingsProvider;
    }

    public async Task<Result<string>> SaveAsWebPAsync(byte[] imageBytes, CancellationToken cancellationToken)
    {
        var storageDirectory = await _settingsProvider.GetStringAsync(
            ImageStorageSettingsKeys.PurchaseInvoiceImagesDirectory, defaultValue: "PurchaseInvoiceImages", cancellationToken);

        try
        {
            Directory.CreateDirectory(storageDirectory!);
        }
        catch (Exception ex)
        {
            return Result.Failure<string>(Error.BusinessRule("Image.StorageDirectoryUnavailable", $"تعذّر الوصول لمجلد التخزين: {ex.Message}"));
        }

        var fileName = $"{Guid.NewGuid()}.webp";
        var filePath = Path.Combine(storageDirectory!, fileName);

        try
        {
            using var image = await Image.LoadAsync(new MemoryStream(imageBytes), cancellationToken);
            await image.SaveAsync(filePath, new WebpEncoder(), cancellationToken);
        }
        catch (Exception ex)
        {
            return Result.Failure<string>(Error.BusinessRule("Image.ConversionFailed", $"تعذّر تحويل/حفظ الصورة: {ex.Message}"));
        }

        return Result.Success(filePath);
    }
}
