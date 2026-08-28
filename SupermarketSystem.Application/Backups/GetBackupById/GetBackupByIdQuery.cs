using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Backups;

namespace SupermarketSystem.Application.Backups.GetBackupById;

public sealed record GetBackupByIdQuery(Guid BackupId);

// المسار الفعلي على القرص يُرجَّع هون عمدًا (بخلاف بقية الـDTOs اللي
// بتتجنّب كشف تفاصيل تخزين داخلية) — لأنه الغرض الوحيد من هذا
// الاستعلام هو تمكين الـAPI layer من قراءة الملف وبثّه للتنزيل. القراءة
// والبث نفسهم مسؤولية الـendpoint (Infrastructure/API)، لا Application.
public sealed record BackupFileDetailsDto(string FileName, string FilePath, BackupStatus Status);

public sealed class GetBackupByIdHandler
{
    private readonly IApplicationDbContext _context;

    public GetBackupByIdHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<BackupFileDetailsDto>> HandleAsync(GetBackupByIdQuery query, CancellationToken cancellationToken)
    {
        var backup = await _context.DatabaseBackups.AsNoTracking()
            .Where(b => b.Id == query.BackupId)
            .Select(b => new BackupFileDetailsDto(b.FileName, b.FilePath, b.Status))
            .FirstOrDefaultAsync(cancellationToken);

        if (backup is null)
        {
            return Result.Failure<BackupFileDetailsDto>(
                Error.NotFound("Backup.NotFound", $"النسخة الاحتياطية '{query.BackupId}' غير موجودة."));
        }

        if (backup.Status != BackupStatus.Completed)
        {
            return Result.Failure<BackupFileDetailsDto>(
                Error.BusinessRule("Backup.NotDownloadable", "هذه النسخة لم تكتمل بنجاح، لا يوجد ملف قابل للتنزيل."));
        }

        return Result.Success(backup);
    }
}
