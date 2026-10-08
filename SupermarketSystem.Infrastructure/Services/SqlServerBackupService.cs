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
        var configuredDirectory = await _settingsProvider.GetStringAsync(
            BackupSettingsKeys.StorageDirectory, defaultValue: string.Empty, cancellationToken);

        var fileName = $"{databaseName}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.bak";
        // Path.Combine هون بيصير مسار من منظور نظام ملفات API، بس التنفيذ
        // الفعلي (BACKUP DATABASE) بيكتب من منظور نظام ملفات SQL Server —
        // نفس التنبيه الموثَّق فوق. لو الاثنان نفس الجهاز (شائع بمنشآت
        // صغيرة)، هذا يشتغل مباشرة بلا أي إعداد إضافي.
        await using var connection = new SqlConnection(_context.Database.GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        // المجلد: لو الإعداد فاضي أو مسار نسبي (مثل "Backups" القديم) نستعمل مجلد النسخ الافتراضي لـSQL Server نفسه
        // (InstanceDefaultBackupPath) - المسار النسبي كان بيتفسّر من منظور SQL Server والمجلد غالبًا مش موجود فالنسخة كانت تفشل
        // (المراجعة النقدية 6/10/2026، بند 3). مسار مطلق بيحدده صاحب المحل بيضل زي ما هو.
        var storageDirectory = !string.IsNullOrWhiteSpace(configuredDirectory) && Path.IsPathRooted(configuredDirectory)
            ? configuredDirectory
            : await GetInstanceDefaultBackupDirectoryAsync(connection, cancellationToken) ?? configuredDirectory;
        if (string.IsNullOrWhiteSpace(storageDirectory))
        {
            throw new InvalidOperationException(
                "ما قدرنا نحدد مجلد النسخ الاحتياطي: الإعداد Backup.StorageDirectory فاضي/نسبي وSQL Server ما رجّع مجلد نسخ افتراضي. حدّد مسار كامل من صفحة الإعدادات.");
        }

        var filePath = Path.Combine(storageDirectory, fileName);

        var sql = $"""
            BACKUP DATABASE [{databaseName}]
            TO DISK = @filePath
            WITH COMPRESSION, INIT, NAME = @backupName;
            """;

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

    public Task CopyToSecondaryAsync(string sourceFilePath, string secondaryDirectory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(sourceFilePath))
        {
            throw new FileNotFoundException(
                "ملف النسخة الأصلية مش ظاهر لحساب تشغيل الـAPI (ممكن SQL Server على جهاز تاني) فما قدرنا ننسخه للمكان الثاني.", sourceFilePath);
        }

        Directory.CreateDirectory(secondaryDirectory);
        File.Copy(sourceFilePath, Path.Combine(secondaryDirectory, Path.GetFileName(sourceFilePath)), overwrite: true);
        return Task.CompletedTask;
    }

    private static async Task<string?> GetInstanceDefaultBackupDirectoryAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(4000));";
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is string path && !string.IsNullOrWhiteSpace(path) ? path : null;
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
