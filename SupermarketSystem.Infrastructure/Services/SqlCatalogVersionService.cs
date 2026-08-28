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
}
