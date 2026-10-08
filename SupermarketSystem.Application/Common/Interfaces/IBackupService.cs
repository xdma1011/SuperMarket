namespace SupermarketSystem.Application.Common.Interfaces;

public sealed record BackupFileInfo(string FileName, string FilePath, long FileSizeBytes);

public static class BackupSettingsKeys
{
    /// <summary>
    /// المجلد اللي فيه تُكتب ملفات .bak. تنبيه مهم: BACKUP DATABASE بتنفَّذ
    /// فعليًا على السيرفر اللي شغّال عليه SQL Server نفسه، لا على السيرفر
    /// اللي شغّال عليه الـAPI — لو الاثنان أجهزة مختلفة، هذا المسار لازم
    /// يكون قابل للوصول والكتابة من طرف حساب خدمة SQL Server تحديدًا.
    /// </summary>
    public const string StorageDirectory = "Backup.StorageDirectory";

    /// <summary>
    /// نسخة ثانية لمكان تاني (فلاشة/مجلد Drive/مشاركة شبكة) - اختياري. النسخ الأولى على نفس الهارد: لو الهارد خرب أو الجهاز انسرق
    /// بتروح القاعدة والنسخ سوا (المراجعة النقدية 6/10/2026، بند 3). المسار لازم يكون قابل للكتابة من حساب تشغيل الـAPI. فاضي = بلا نسخة ثانية.
    /// ما بينظّف تلقائيًا (بخلاف الأولى) - الإدارة بتنظّفه يدويًا.
    /// </summary>
    public const string SecondaryDirectory = "Backup.SecondaryDirectory";

    /// <summary>عدد النسخ المحفوظة قبل حذف الأقدم تلقائيًا.</summary>
    public const string RetentionCount = "Backup.RetentionCount";
}

/// <summary>
/// إنشاء نسخة احتياطية فعلية هو عملية SQL Server خام (BACKUP DATABASE)
/// + تعامل مع نظام الملفات — تفاصيل Infrastructure بحتة، بلا مكان لها
/// بطبقة Application غير هذا الـinterface.
/// </summary>
public interface IBackupService
{
    Task<BackupFileInfo> CreateBackupAsync(CancellationToken cancellationToken);

    /// <summary>ينسخ ملف نسخة ناجحة لمجلد ثانوي (بيرمي استثناء لو فشل - المستدعي بينبّه ولا بيفشّل النسخة الأصلية).</summary>
    Task CopyToSecondaryAsync(string sourceFilePath, string secondaryDirectory, CancellationToken cancellationToken);

    /// <summary>يحذف ملفات النسخ المحدَّدة من القرص. يرجّع أسماء الملفات المحذوفة فعليًا.</summary>
    Task<IReadOnlyList<string>> DeleteBackupFilesAsync(IReadOnlyList<string> filePaths, CancellationToken cancellationToken);
}
