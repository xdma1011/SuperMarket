using SupermarketSystem.Domain.Common;

namespace SupermarketSystem.Domain.Backups;

public enum BackupStatus
{
    Completed = 1,
    Failed = 2
}

/// <summary>
/// سجل تتبّع لكل عملية نسخ احتياطي — اسم الملف، مساره، حجمه، حالته.
///
/// ملاحظة مهمة: هذا كيان جديد كليًا، مش إحياء لـ"BackupLog" اللي استُبعِد
/// عمدًا من Phase A ("concern تشغيلي لا مفهوم بزنس"). الفرق: بالمرحلة
/// الأولى كان الاستبعاد صحيح لأنه ما كانت في ميزة فعلية تحتاجه (بناء شيء
/// لمجرد الاكتمال). الآن في ميزة حقيقية (endpoint تنزيل/تشغيل نسخة
/// احتياطية) محتاجة سجل فعلي تشتغل عليه — القرار تغيّر لأنه السياق تغيّر،
/// مش لأنه القرار الأول كان غلط.
///
/// أصلًا Entity بسيط (لا AuditableEntity) — عملية النسخ تُنشأ مرة وحدة
/// وما بتتعدَّل، بلا حاجة لحقول تعديل.
/// </summary>
public class DatabaseBackup : Entity
{
    public string FileName { get; private set; } = null!;
    public string FilePath { get; private set; } = null!;
    public long FileSizeBytes { get; private set; }
    public BackupStatus Status { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    private DatabaseBackup() { } // EF Core

    private DatabaseBackup(string fileName, string filePath, long fileSizeBytes, BackupStatus status, string? errorMessage, DateTime createdAtUtc)
    {
        FileName = fileName;
        FilePath = filePath;
        FileSizeBytes = fileSizeBytes;
        Status = status;
        ErrorMessage = errorMessage;
        CreatedAtUtc = createdAtUtc;
    }

    public static DatabaseBackup Succeeded(string fileName, string filePath, long fileSizeBytes, DateTime createdAtUtc)
        => new(fileName, filePath, fileSizeBytes, BackupStatus.Completed, errorMessage: null, createdAtUtc);

    public static DatabaseBackup Failed(string fileName, string errorMessage, DateTime createdAtUtc)
        => new(fileName, filePath: string.Empty, fileSizeBytes: 0, BackupStatus.Failed, errorMessage, createdAtUtc);
}
