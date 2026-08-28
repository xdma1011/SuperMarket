using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Infrastructure.Persistence;

namespace SupermarketSystem.Infrastructure.Services;

/// <summary>
/// نسخة SQL Server أصلية (BACKUP DATABASE ... WITH COMPRESSION) — أسرع
/// وأوثق من أي بديل مبني يدويًا (export/import مخصص)، ومدعومة بالاستعادة
/// المباشرة (RESTORE DATABASE) بلا أي أداة إضافية.
/// </summary>
public sealed class SqlServerBackupService : IBackupService
{
    private readonly AppDbContext _context;
    private readonly ISettingsProvider _settingsProvider;

    public SqlServerBackupService(AppDbContext context, ISettingsProvider settingsProvider)
    {
        _context = context;
        _settingsProvider = settingsProvider;
    }

    public async Task<BackupFileInfo> CreateBackupAsync(CancellationToken cancellationToken)
    {
        var databaseName = GetDatabaseName();
        var storageDirectory = await _settingsProvider.GetStringAsync(
            BackupSettingsKeys.StorageDirectory, defaultValue: "Backups", cancellationToken);

        var fileName = $"{databaseName}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.bak";
        // Path.Combine هون بيصير مسار من منظور نظام ملفات API، بس التنفيذ
        // الفعلي (BACKUP DATABASE) بيكتب من منظور نظام ملفات SQL Server —
        // نفس التنبيه الموثَّق فوق. لو الاثنان نفس الجهاز (شائع بمنشآت
        // صغيرة)، هذا يشتغل مباشرة بلا أي إعداد إضافي.
        var filePath = Path.Combine(storageDirectory!, fileName);

        var sql = $"""
            BACKUP DATABASE [{databaseName}]
            TO DISK = @filePath
            WITH COMPRESSION, INIT, NAME = @backupName;
            """;

        await using var connection = new SqlConnection(_context.Database.GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        // مهلة أطول من الافتراضي — نسخ قواعد بيانات كبيرة ممكن ياخد وقت
        // أطول من الـ30 ثانية الافتراضية لـSqlCommand.
        command.CommandTimeout = 600;
        command.Parameters.Add(new SqlParameter("@filePath", filePath));
        command.Parameters.Add(new SqlParameter("@backupName", $"{databaseName}-Full-{DateTime.UtcNow:yyyy-MM-dd}"));

        await command.ExecuteNonQueryAsync(cancellationToken);

        // الحجم الحقيقي بعد الضغط — يُقرأ من نظام الملفات مباشرة، لا يُخمَّن.
        var fileSizeBytes = File.Exists(filePath) ? new FileInfo(filePath).Length : 0;

        return new BackupFileInfo(fileName, filePath, fileSizeBytes);
    }

    public Task<IReadOnlyList<string>> DeleteBackupFilesAsync(IReadOnlyList<string> filePaths, CancellationToken cancellationToken)
    {
        var deleted = new List<string>();

        foreach (var path in filePaths)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    deleted.Add(path);
                }
            }
            catch
            {
                // حذف فاشل لملف وحد (قفل، صلاحيات) ما لازم يوقف تنظيف
                // الباقي — بيضل مسجَّل بقاعدة البيانات وممكن يُعاد المحاولة لاحقًا.
            }
        }

        return Task.FromResult<IReadOnlyList<string>>(deleted);
    }

    private string GetDatabaseName()
    {
        var connectionString = _context.Database.GetConnectionString()
            ?? throw new InvalidOperationException("لا يوجد connection string مُعدّ.");

        var builder = new SqlConnectionStringBuilder(connectionString);
        return builder.InitialCatalog;
    }
}
