using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Infrastructure.Persistence;

namespace SupermarketSystem.Infrastructure.Services;

/// <summary>
/// نفس نمط CachedSettingsProvider بالضبط (IMemoryCache داخل العملية،
/// كافٍ لـmodular monolith وحيد النسخة). فرق واحد مقصود: مدة الكاش هون
/// 60 ثانية لا 5 دقائق — سحب صلاحية موظف بعد إنهاء خدمته حالة أحسّ بكثير
/// من تعديل إعداد عادي، فنافذة "لسه شغّالة رغم السحب" لازم تكون أقصر.
///
/// آمن يستخدم AppDbContext مباشرة (لا اعتمادية دائرية): RealCurrentUserContext
/// (اللي AppDbContext بيعتمد عليه) بيستخدم IHttpContextAccessor بس، أبدًا
/// ISettingsProvider ولا IPermissionChecker — فما في دورة هون.
/// </summary>
public sealed class CachedPermissionChecker : IPermissionChecker
{
    private const string CacheKeyPrefix = "userpermissions:";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    private readonly IMemoryCache _cache;
    private readonly AppDbContext _context;

    public CachedPermissionChecker(IMemoryCache cache, AppDbContext context)
    {
        _cache = cache;
        _context = context;
    }

    public async Task<bool> HasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken)
    {
        var permissions = await GetUserPermissionCodesAsync(userId, cancellationToken);
        return permissions.Contains(permissionCode);
    }

    private async Task<HashSet<string>> GetUserPermissionCodesAsync(Guid userId, CancellationToken cancellationToken)
    {
        var cacheKey = CacheKeyPrefix + userId;

        if (_cache.TryGetValue(cacheKey, out HashSet<string>? cached) && cached is not null)
        {
            return cached;
        }

        var codes = await _context.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == userId)
            .Join(_context.RolePermissions.AsNoTracking(), ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => rp.PermissionId)
            .Join(_context.Permissions.AsNoTracking(), pid => pid, p => p.Id, (pid, p) => p.Code)
            .Distinct()
            .ToListAsync(cancellationToken);

        var result = codes.ToHashSet();
        _cache.Set(cacheKey, result, CacheDuration);

        return result;
    }
}
