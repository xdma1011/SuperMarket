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

    /// <summary>يحذف ملفات النسخ المحدَّدة من القرص. يرجّع أسماء الملفات المحذوفة فعليًا.</summary>
    Task<IReadOnlyList<string>> DeleteBackupFilesAsync(IReadOnlyList<string> filePaths, CancellationToken cancellationToken);
}
