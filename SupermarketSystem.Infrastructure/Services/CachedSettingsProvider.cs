using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Infrastructure.Persistence;

namespace SupermarketSystem.Infrastructure.Services;

/// <summary>
/// IMemoryCache-backed settings provider. In-process cache is the right
/// choice for a single-instance modular monolith (brief §37) — a distributed
/// cache would be premature infrastructure for a supermarket back office.
///
/// If this is ever scaled out to multiple instances, Invalidate() becomes
/// per-instance and settings changes would take up to CacheDuration to
/// propagate to other nodes. That is the reason the absolute expiry below is
/// short (5 minutes) rather than hours: it bounds the staleness window even
/// when invalidation cannot reach every node.
/// </summary>
public sealed class CachedSettingsProvider : ISettingsProvider
{
    private const string CacheKeyPrefix = "systemsetting:";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    // Tracks which keys this instance has cached, so InvalidateAll can clear
    // them — IMemoryCache offers no enumeration of its own contents.
    private static readonly HashSet<string> TrackedKeys = new();
    private static readonly object TrackedKeysLock = new();

    private readonly IMemoryCache _cache;
    private readonly AppDbContext _context;

    public CachedSettingsProvider(IMemoryCache cache, AppDbContext context)
    {
        _cache = cache;
        _context = context;
    }

    public async Task<bool> GetBoolAsync(string key, bool defaultValue, CancellationToken cancellationToken)
    {
        var raw = await GetRawAsync(key, cancellationToken);
        return bool.TryParse(raw, out var parsed) ? parsed : defaultValue;
    }

    public async Task<decimal> GetDecimalAsync(string key, decimal defaultValue, CancellationToken cancellationToken)
    {
        var raw = await GetRawAsync(key, cancellationToken);

        // InvariantCulture: a setting stored as "10.5" must never be parsed
        // as 105 on a server with a comma decimal separator.
        return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : defaultValue;
    }

    public async Task<string?> GetStringAsync(string key, string? defaultValue, CancellationToken cancellationToken)
        => await GetRawAsync(key, cancellationToken) ?? defaultValue;

    private async Task<string?> GetRawAsync(string key, CancellationToken cancellationToken)
    {
        var cacheKey = CacheKeyPrefix + key;

        if (_cache.TryGetValue(cacheKey, out string? cached))
        {
            return cached;
        }

        // AsNoTracking: read-only lookup, never mutated through this path.
        var value = await _context.SystemSettings
            .AsNoTracking()
            .Where(s => s.Key == key)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);

        // A missing setting is cached as null too, so an unset key doesn't
        // hit the database on every checkout.
        _cache.Set(cacheKey, value, CacheDuration);

        lock (TrackedKeysLock)
        {
            TrackedKeys.Add(cacheKey);
        }

        return value;
    }

    public void Invalidate(string key) => _cache.Remove(CacheKeyPrefix + key);

    public void InvalidateAll()
    {
        lock (TrackedKeysLock)
        {
            foreach (var cacheKey in TrackedKeys)
            {
                _cache.Remove(cacheKey);
            }

            TrackedKeys.Clear();
        }
    }
}
