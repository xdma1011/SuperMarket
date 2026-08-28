namespace SupermarketSystem.Application.Common.Interfaces;

/// <summary>
/// Cached access to SystemSetting values.
///
/// Caching scope is deliberately narrow (brief §20): system settings are
/// low-change reference data, which is exactly the category that SHOULD be
/// cached. Transactional data — stock levels, invoice totals, cash balances
/// — is never cached anywhere in this system, because stale values there
/// cause financial and inventory errors.
///
/// Cache contract (every cache in this system must state all three):
///   - Expiration:   absolute expiry, so a missed invalidation self-heals.
///   - Invalidation: Invalidate()/InvalidateAll() called by the settings
///                   update use case, so an admin's change takes effect
///                   immediately rather than after the expiry window.
///   - Ownership:    this interface's implementation is the sole owner; no
///                   other component reads or writes the settings cache.
/// </summary>
public interface ISettingsProvider
{
    Task<bool> GetBoolAsync(string key, bool defaultValue, CancellationToken cancellationToken);
    Task<decimal> GetDecimalAsync(string key, decimal defaultValue, CancellationToken cancellationToken);
    Task<string?> GetStringAsync(string key, string? defaultValue, CancellationToken cancellationToken);

    void Invalidate(string key);
    void InvalidateAll();
}
