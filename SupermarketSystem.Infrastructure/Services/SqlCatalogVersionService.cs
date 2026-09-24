using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Infrastructure.Persistence;

namespace SupermarketSystem.Infrastructure.Services;

/// <summary>
/// المفتاح "Catalog.Version" مُخزَّن بجدول SystemSettings الموجود أصلًا.
/// الزيادة الذرية بجملة UPDATE خام، تفاديًا لفقدان زيادة لو تعديلان على
/// الكتالوج صاروا بنفس اللحظة تقريبًا.
/// </summary>
public sealed class SqlCatalogVersionService : ICatalogVersionService
{
    private const string VersionKey = "Catalog.Version";
    private readonly AppDbContext _context;

    public SqlCatalogVersionService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<long> GetCurrentVersionAsync(CancellationToken cancellationToken)
    {
        var value = await _context.SystemSettings.AsNoTracking()
            .Where(s => s.Key == VersionKey)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);

        return value is not null && long.TryParse(value, out var version) ? version : 0L;
    }

    public async Task IncrementVersionAsync(CancellationToken cancellationToken)
    {
        await _context.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE [SystemSettings]
            SET [Value] = CAST((TRY_CAST([Value] AS BIGINT) + 1) AS NVARCHAR(50))
            WHERE [Key] = {VersionKey}", cancellationToken);
    }

    public async Task<long> IncrementVersionAndGetAsync(CancellationToken cancellationToken)
    {
        var updated = await _context.Database.SqlQuery<long>($@"
            UPDATE [SystemSettings]
            SET [Value] = CAST((ISNULL(TRY_CAST([Value] AS BIGINT), 0) + 1) AS NVARCHAR(50))
            OUTPUT CAST(inserted.[Value] AS BIGINT) AS [Value]
            WHERE [Key] = {VersionKey}").ToListAsync(cancellationToken);

        if (updated.Count > 0)
        {
            return updated[0];
        }

        // الصف مبذور بالـmigrations، بس لو انحذف (أو بيئة اختبار بتصفّر الإعدادات)
        // بنرجّعه بدل ما تضيع الزيادة بصمت.
        await _context.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO [SystemSettings] ([Id], [Key], [Value], [Description], [CreatedAtUtc])
            VALUES (NEWID(), {VersionKey}, N'1', N'Global catalog version counter.', SYSUTCDATETIME())", cancellationToken);
        return 1L;
    }
}
