using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Backups;

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

    public TriggerBackupHandler(
        IApplicationDbContext context,
        IBackupService backupService,
        ISettingsProvider settingsProvider,
        IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _backupService = backupService;
        _settingsProvider = settingsProvider;
        _dateTimeProvider = dateTimeProvider;
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

            return Result.Failure<TriggerBackupResponse>(
                Error.BusinessRule("Backup.Failed", $"فشلت عملية النسخ الاحتياطي: {ex.Message}"));
        }

        _context.DatabaseBackups.Add(backupRecord);
        await _context.SaveChangesAsync(cancellationToken);

        var deletedFileNames = await CleanupOldBackupsAsync(cancellationToken);

        return Result.Success(new TriggerBackupResponse(
            backupRecord.Id, backupRecord.FileName, backupRecord.FileSizeBytes, deletedFileNames));
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
