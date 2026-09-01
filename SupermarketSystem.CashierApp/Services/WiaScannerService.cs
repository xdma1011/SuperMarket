namespace SupermarketSystem.CashierApp.Services;

public sealed record ScanResult(bool Success, byte[]? ImageBytes, string? FileName, string? ErrorMessage);

/// <summary>
/// مسح ضوئي اختياري عبر WIA (Windows Image Acquisition) — الميكانيزم
/// القياسي بويندوز لسكانرات المستندات المكتبية. الربط late-bound عبر
/// Type.GetTypeFromProgID بدل COM reference بمشروع الـcsproj عمدًا:
/// إضافة COM reference بتحتاج تسجيل نوع WIA على كل جهاز بناء (dev
/// machine)، بينما late binding يشتغل وقت التشغيل بس على أي جهاز
/// ويندوز عادي، بلا أي متطلب إضافي وقت البناء. "لو بدو يضيف وكانت
/// متوفرة السكانر" (طلب صاحب المشروع) - لو ما في سكانر أو المستخدم
/// ألغى الحوار، بيرجع Success=false بهدوء، بلا Exception غير متوقَّعة.
/// </summary>
public static class WiaScannerService
{
    private const string CommonDialogProgId = "WIA.CommonDialog";

    // ثوابت WIA (WiaImageIntent/WiaImageBias) - قيم رسمية من WIA Automation Layer.
    private const int WiaIntentUnspecified = 0;
    private const int WiaBiasMinimizeSize = 65536;

    public static ScanResult TryScan()
    {
        var dialogType = Type.GetTypeFromProgID(CommonDialogProgId);
        if (dialogType is null)
        {
            return new ScanResult(false, null, null, "مكوّن WIA غير متوفر على هذا الجهاز.");
        }

        object? dialog = null;
        object? imageFile = null;

        try
        {
            dialog = Activator.CreateInstance(dialogType);
            if (dialog is null)
            {
                return new ScanResult(false, null, null, "تعذّر تشغيل حوار المسح الضوئي.");
            }

            // ShowAcquireImage(DeviceType, Intent, Bias, FormatID, AlwaysSelectDevice, UseCommonUI, CancelError)
            // معاملات اختيارية بالـCOM API الأصلي - late-bound dynamic بيمررهم Missing.Value ضمنيًا.
            dynamic dynamicDialog = dialog;
            imageFile = dynamicDialog.ShowAcquireImage(
                1, // WiaDeviceType.ScannerDeviceType
                WiaIntentUnspecified,
                WiaBiasMinimizeSize,
                "{B96B3CAE-0728-11D3-9D7B-0000F81EF32E}", // wiaFormatJPEG
                false, // AlwaysSelectDevice
                true,  // UseCommonUI
                false  // CancelError - false يعني إلغاء المستخدم يرجّع null بدل Exception
            );

            if (imageFile is null)
            {
                return new ScanResult(false, null, null, "تم إلغاء المسح الضوئي أو لا يوجد سكانر متصل.");
            }

            var tempPath = Path.Combine(Path.GetTempPath(), $"scan_{Guid.NewGuid()}.jpg");
            dynamic dynamicImage = imageFile;
            dynamicImage.SaveFile(tempPath);

            var bytes = File.ReadAllBytes(tempPath);
            try { File.Delete(tempPath); } catch { /* تنظيف اختياري - فشل حذف ملف مؤقت لا يستحق إفشال العملية */ }

            return new ScanResult(true, bytes, Path.GetFileName(tempPath), null);
        }
        catch (Exception ex)
        {
            return new ScanResult(false, null, null, $"تعذّر المسح الضوئي: {ex.Message}");
        }
        finally
        {
            if (imageFile is not null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(imageFile);
            if (dialog is not null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(dialog);
        }
    }
}
