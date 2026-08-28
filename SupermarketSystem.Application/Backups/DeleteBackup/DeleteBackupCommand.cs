using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Backups;

namespace SupermarketSystem.Application.Backups.DeleteBackup;

public sealed record DeleteBackupCommand(Guid BackupId);

/// <summary>
/// يحذف نسخة احتياطية محدَّدة (الملف من القرص + السجل من قاعدة البيانات).
///
/// قيد أساسي: آخر نسخة ناجحة (Completed) لا يمكن حذفها أبدًا — هي شبكة
/// الأمان الوحيدة الموجودة فعليًا لحظة الطلب. لو انحذفت والنظام بعدها
/// تعطّل قبل ما تُنشأ نسخة جديدة، ما رح يبقى أي نقطة استعادة إطلاقًا.
/// هذا القيد يُفحص وقت كل طلب حذف (لا وقت الإنشاء)، لأنه "آخر نسخة"
/// تتغيّر تلقائيًا كل ما تُنشأ نسخة جديدة — نفس النسخة اللي محمية اليوم
/// ممكن تصير قابلة للحذف بمجرد ما نسخة أحدث منها تُنشأ بنجاح.
///
/// نسخ فاشلة (Status = Failed) غير محمية أبدًا — هي سجلات فشل بلا ملف
/// فعلي، لا تشكّل أي نقطة استعادة أصلًا.
/// </summary>
public sealed class DeleteBackupHandler
{
    private readonly IApplicationDbContext _context;
    private readonly IBackupService _backupService;

    public DeleteBackupHandler(IApplicationDbContext context, IBackupService backupService)
    {
        _context = context;
        _backupService = backupService;
    }

    public async Task<Result> HandleAsync(DeleteBackupCommand command, CancellationToken cancellationToken)
    {
        var backup = await _context.DatabaseBackups
            .FirstOrDefaultAsync(b => b.Id == command.BackupId, cancellationToken);

        if (backup is null)
        {
            return Result.Failure(Error.NotFound("Backup.NotFound", $"النسخة الاحتياطية '{command.BackupId}' غير موجودة."));
        }

        if (backup.Status == BackupStatus.Completed)
        {
            var mostRecentCompletedId = await _context.DatabaseBackups.AsNoTracking()
                .Where(b => b.Status == BackupStatus.Completed)
                .OrderByDescending(b => b.CreatedAtUtc)
                .Select(b => b.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (backup.Id == mostRecentCompletedId)
            {
                return Result.Failure(Error.BusinessRule(
                    "Backup.CannotDeleteMostRecent",
                    "لا يمكن حذف آخر نسخة احتياطية ناجحة — هي شبكة الأمان الوحيدة حاليًا. أنشئ نسخة جديدة أولًا إذا أردت حذف هذه."));
            }
        }

        if (!string.IsNullOrEmpty(backup.FilePath))
        {
            await _backupService.DeleteBackupFilesAsync(new[] { backup.FilePath }, cancellationToken);
        }

        _context.DatabaseBackups.Remove(backup);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
