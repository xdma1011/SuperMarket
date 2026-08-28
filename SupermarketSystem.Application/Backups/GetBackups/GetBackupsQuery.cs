using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Domain.Backups;

namespace SupermarketSystem.Application.Backups.GetBackups;

public sealed record GetBackupsQuery(PagedRequest Paging);

/// <summary>
/// Code: رقم الحالة الثابت (لا يتغيّر أبدًا، آمن نقارنه بالفرونت إند
/// حرفيًا: 1 = مكتملة، 2 = فشلت). CodeTitle: اسم مقروء بالعربي، للعرض
/// المباشر بلا حاجة الفرونت إند يترجم الرقم بنفسه.
///
/// ═══════════════════════════════════════════════════════════════════
/// هذا هو الحل لمشكلة "الحالة بتظهر فشلت رغم نجاح النسخة الاحتياطية":
/// ═══════════════════════════════════════════════════════════════════
/// كنا نرجّع BackupStatus (enum C#) مباشرة بالـJSON — النظام الافتراضي
/// لتسلسل enum بـ.NET يحوّلها لنص (اسم العضو بالإنجليزي، "Completed")
/// لا رقم. الفرونت إند كان يقارنها بـ`=== 1` (رقم)، فالمقارنة كانت
/// دائمًا false. Code هون رقم صريح مضمون، لا يعتمد على سلوك تسلسل
/// افتراضي قابل للتغيّر.
/// </summary>
public sealed record BackupItemDto(
    Guid Id,
    string FileName,
    long FileSizeBytes,
    int StatusCode,
    string StatusTitle,
    string? ErrorMessage,
    DateTime CreatedAtUtc);

public sealed record BackupStatsDto(int TotalCount, long TotalSizeBytes);

public sealed record GetBackupsResponse(PagedResult<BackupItemDto> Items, BackupStatsDto Stats);

public sealed class GetBackupsHandler
{
    private readonly IApplicationDbContext _context;

    public GetBackupsHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<GetBackupsResponse> HandleAsync(GetBackupsQuery query, CancellationToken cancellationToken)
    {
        var paging = query.Paging.Normalized();

        var backups = _context.DatabaseBackups.AsNoTracking()
            .OrderByDescending(b => b.CreatedAtUtc);

        var totalCount = await backups.CountAsync(cancellationToken);

        // الإحصائيات على المجموعة الناجحة فقط — نسخة فاشلة بلا ملف فعلي
        // (حجمها صفر دائمًا) ما لازم تُحسب ضمن "المساحة المستخدمة فعليًا".
        var successfulBackups = _context.DatabaseBackups.AsNoTracking()
            .Where(b => b.Status == BackupStatus.Completed);

        var stats = new BackupStatsDto(
            await successfulBackups.CountAsync(cancellationToken),
            await successfulBackups.SumAsync(b => b.FileSizeBytes, cancellationToken));

        var items = await backups
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Select(b => new BackupItemDto(
                b.Id, b.FileName, b.FileSizeBytes,
                (int)b.Status,
                b.Status == BackupStatus.Completed ? "مكتملة" : "فشلت",
                b.ErrorMessage, b.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        var pagedItems = new PagedResult<BackupItemDto>(items, totalCount, paging.PageNumber, paging.PageSize);

        return new GetBackupsResponse(pagedItems, stats);
    }
}

