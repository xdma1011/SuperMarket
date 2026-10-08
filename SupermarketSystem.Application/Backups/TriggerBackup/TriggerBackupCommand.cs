using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Notifications;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Backups;
using SupermarketSystem.Domain.Notifications;

namespace SupermarketSystem.Application.Backups.TriggerBackup;

public sealed record TriggerBackupCommand;

public sealed record TriggerBackupResponse(
    Guid BackupId,
    string FileName,
    long FileSizeBytes,
    // أسماء الملفات اللي انحذفت تلقائيًا بهذا التشغيل بسبب تجاوز حد
    // الاحتفاظ — شفافية كاملة، لا حذف صامت بلا أثر بالرد.
    IReadOnlyList<string> DeletedOldBackupFileNames);

/// <summary>
/// ينشئ نسخة احتياطية جديدة، يسجّلها، وينظّف القديم اللي تجاوز حد
/// الاحتفاظ — كل هذا بعملية واحدة، بدل ما ننسى نستدعي التنظيف لحاله.
///
/// فشل النسخ نفسه ما يُترجم لاستثناء يوقف الطلب — يُسجَّل صف Failed
/// بجدول DatabaseBackups (شفافية: تقدر تشوف "حاولنا وفشلنا"، مش سكوت
/// كامل)، ويرجع خطأ واضح للمستدعي.
/// </summary>
public sealed class TriggerBackupHandler
{
    private readonly IApplicationDbContext _context;
    private readonly IBackupService _backupService;
    private readonly ISettingsProvider _settingsProvider;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly INotificationDispatcher _notificationDispatcher;

    public TriggerBackupHandler(
        IApplicationDbContext context,
        IBackupService backupService,
        ISettingsProvider settingsProvider,
        IDateTimeProvider dateTimeProvider,
        INotificationDispatcher notificationDispatcher)
    {
        _context = context;
        _backupService = backupService;
        _settingsProvider = settingsProvider;
        _dateTimeProvider = dateTimeProvider;
        _notificationDispatcher = notificationDispatcher;
    }

    private const string BackupFailedTitle = "فشلت النسخة الاحتياطية";
    private const string SecondaryFailedTitle = "فشل نسخ النسخة الاحتياطية للمكان الثاني";

    /// <summary>تنبيه واحد كل ~20 ساعة لنفس العنوان - الخدمة اليومية بتعيد المحاولة، ما بدنا إغراق.</summary>
    private async Task NotifyOncePerDayAsync(
        string title, string body, NotificationSeverity severity, CancellationToken cancellationToken)
    {
        var since = _dateTimeProvider.UtcNow.AddHours(-20);
        var alreadyNotified = await _context.Notifications.AsNoTracking()
            .AnyAsync(n => n.Title == title && n.CreatedAtUtc > since, cancellationToken);
        if (!alreadyNotified)
        {
            await _notificationDispatcher.NotifyAsync(title, body, cancellationToken, severity, link: "/backup");
        }
    }

    public async Task<Result<TriggerBackupResponse>> HandleAsync(TriggerBackupCommand command, CancellationToken cancellationToken)
    {
        DatabaseBackup backupRecord;

        try
        {
            var fileInfo = await _backupService.CreateBackupAsync(cancellationToken);
            backupRecord = DatabaseBackup.Succeeded(
                fileInfo.FileName, fileInfo.FilePath, fileInfo.FileSizeBytes, _dateTimeProvider.UtcNow);
        }
        catch (Exception ex)
        {
            backupRecord = DatabaseBackup.Failed("(فشل قبل تسمية الملف)", ex.Message, _dateTimeProvider.UtcNow);
            _context.DatabaseBackups.Add(backupRecord);
            await _context.SaveChangesAsync(cancellationToken);

            // فشل النسخة كان بس LogWarning بالـconsole = ممكن أيام بلا نسخة وما حدا يعرف (المراجعة النقدية 6/10/2026، بند 3).
            await NotifyOncePerDayAsync(
                BackupFailedTitle,
                $"السبب: {ex.Message}\nتأكد إن مجلد النسخ (Backup.StorageDirectory) موجود وحساب خدمة SQL Server عنده صلاحية كتابة عليه، " +
                "وإن في مساحة فاضية. لحد ما تنصلح ما في نسخة احتياطية جديدة.",
                NotificationSeverity.Critical, cancellationToken);

            return Result.Failure<TriggerBackupResponse>(
                Error.BusinessRule("Backup.Failed", $"فشلت عملية النسخ الاحتياطي: {ex.Message}"));
        }

        _context.DatabaseBackups.Add(backupRecord);
        await _context.SaveChangesAsync(cancellationToken);

        await CopyToSecondaryIfConfiguredAsync(backupRecord, cancellationToken);

        var deletedFileNames = await CleanupOldBackupsAsync(cancellationToken);

        return Result.Success(new TriggerBackupResponse(
            backupRecord.Id, backupRecord.FileName, backupRecord.FileSizeBytes, deletedFileNames));
    }

    private async Task CopyToSecondaryIfConfiguredAsync(DatabaseBackup backupRecord, CancellationToken cancellationToken)
    {
        var secondaryDirectory = await _settingsProvider.GetStringAsync(
            BackupSettingsKeys.SecondaryDirectory, defaultValue: string.Empty, cancellationToken);
        if (string.IsNullOrWhiteSpace(secondaryDirectory))
        {
            return;
        }

        try
        {
            await _backupService.CopyToSecondaryAsync(backupRecord.FilePath, secondaryDirectory.Trim(), cancellationToken);
        }
        catch (Exception ex)
        {
            // النسخة الأولى نجحت - فشل الثانية بينبّه بس، ما بيفشّل العملية.
            await NotifyOncePerDayAsync(
                SecondaryFailedTitle,
                $"المجلد الثاني: {secondaryDirectory}\nالسبب: {ex.Message}\nالنسخة الأصلية نجحت، بس ما انحفظت نسخة بالمكان الثاني.",
                NotificationSeverity.Warning, cancellationToken);
        }
    }

    private async Task<IReadOnlyList<string>> CleanupOldBackupsAsync(CancellationToken cancellationToken)
    {
        var retentionCount = (int)await _settingsProvider.GetDecimalAsync(
            BackupSettingsKeys.RetentionCount, defaultValue: 30m, cancellationToken);

        if (retentionCount <= 0)
        {
            // 0 أو أقل = تعطيل التنظيف التلقائي كليًا — قرار إداري صريح،
            // لا حذف قسري.
            return Array.Empty<string>();
        }

        var successfulBackups = await _context.DatabaseBackups.AsNoTracking()
            .Where(b => b.Status == BackupStatus.Completed)
            .OrderByDescending(b => b.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var toDelete = successfulBackups.Skip(retentionCount).ToList();
        if (toDelete.Count == 0)
        {
            return Array.Empty<string>();
        }

        var deletedPaths = await _backupService.DeleteBackupFilesAsync(
            toDelete.Select(b => b.FilePath).ToList(), cancellationToken);

        var deletedIds = toDelete.Where(b => deletedPaths.Contains(b.FilePath)).Select(b => b.Id).ToList();
        var trackedToRemove = await _context.DatabaseBackups.Where(b => deletedIds.Contains(b.Id)).ToListAsync(cancellationToken);
        _context.DatabaseBackups.RemoveRange(trackedToRemove);
        await _context.SaveChangesAsync(cancellationToken);

        return trackedToRemove.Select(b => b.FileName).ToList();
    }
}
