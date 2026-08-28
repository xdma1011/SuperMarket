using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Domain.Identity;

namespace SupermarketSystem.Application.Authentication.GetActiveSessions;

public sealed record GetActiveSessionsQuery(PagedRequest Paging, Guid? UserId);

public sealed record ActiveSessionItemDto(
    Guid SessionId,
    Guid UserId,
    string Username,
    ClientAppType AppType,
    Guid? BranchId,
    string? IpAddress,
    string? DeviceInfo,
    DateTime CreatedAtUtc,
    DateTime? LastRefreshedAtUtc,
    DateTime ExpiresAtUtc);

/// <summary>
/// "فعّالة" هون تحديدًا = ما انبطلت ولا انتهت (نفس شرط
/// UserSession.IsActive، معاد كتابته بـLINQ قابل للترجمة بدل استدعاء
/// الميثود نفسها — EF Core ما بيقدر يترجم استدعاء ميثود Domain عادي
/// لجملة SQL).
///
/// هذا أول استخدام فعلي لـIpAddress وDeviceInfo المسجَّلين بالجلسة —
/// كانا مطلبًا من القائمة الأصلية وناقصين بالكامل قبل D11. من هون
/// بالضبط مدير النظام بيقدر يلاحظ جلسة بـIP أو جهاز غريب ويوقفها فورًا
/// عبر RevokeSessionHandler.
/// </summary>
public sealed class GetActiveSessionsHandler
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _dateTimeProvider;

    public GetActiveSessionsHandler(IApplicationDbContext context, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<PagedResult<ActiveSessionItemDto>> HandleAsync(GetActiveSessionsQuery query, CancellationToken cancellationToken)
    {
        var paging = query.Paging.Normalized();
        var utcNow = _dateTimeProvider.UtcNow;

        var sessions = _context.UserSessions.AsNoTracking()
            .Where(s => s.RevokedAtUtc == null && s.ExpiresAtUtc > utcNow);

        if (query.UserId is { } userId)
        {
            sessions = sessions.Where(s => s.UserId == userId);
        }

        sessions = sessions.OrderByDescending(s => s.CreatedAtUtc);

        var totalCount = await sessions.CountAsync(cancellationToken);

        var items = await sessions
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Join(_context.Users.AsNoTracking().IgnoreQueryFilters(),
                s => s.UserId, u => u.Id,
                (s, u) => new ActiveSessionItemDto(
                    s.Id, s.UserId, u.Username, s.AppType, s.BranchId, s.IpAddress, s.DeviceInfo,
                    s.CreatedAtUtc, s.LastRefreshedAtUtc, s.ExpiresAtUtc))
            .ToListAsync(cancellationToken);

        return new PagedResult<ActiveSessionItemDto>(items, totalCount, paging.PageNumber, paging.PageSize);
    }
}
