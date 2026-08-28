using SupermarketSystem.Domain.Common;

namespace SupermarketSystem.Domain.Settings;

/// <summary>
/// Aggregate root, global, admin-managed key-value configuration.
/// Cacheable (relatively stable data) per Architecture Review §20.
/// </summary>
public class SystemSetting : AuditableEntity, IHasRowVersion
{
    public string Key { get; private set; } = null!;
    public string Value { get; private set; } = null!;
    public string? Description { get; private set; }
    public byte[]? RowVersion { get; private set; }

    private SystemSetting() { } // EF Core

    public SystemSetting(string key, string value, string? description)
    {
        Key = key;
        Value = value;
        Description = description;
    }

    public void UpdateValue(string value) => Value = value;
}

/// <summary>
/// Aggregate root, user-owned (e.g. UI preferences, default branch).
/// Independent entity, not loaded as part of the User aggregate.
/// </summary>
public class UserSetting : Entity
{
    public Guid UserId { get; private set; }
    public string Key { get; private set; } = null!;
    public string Value { get; private set; } = null!;

    private UserSetting() { } // EF Core

    public UserSetting(Guid userId, string key, string value)
    {
        UserId = userId;
        Key = key;
        Value = value;
    }

    public void UpdateValue(string value) => Value = value;
}

/// <summary>
/// Aggregate root, user-owned. Controls which notifications a user receives
/// and via which channel.
/// </summary>
public class NotificationSetting : Entity
{
    public Guid UserId { get; private set; }
    public string NotificationCode { get; private set; } = null!;
    public bool IsEnabled { get; private set; }

    private NotificationSetting() { } // EF Core

    public NotificationSetting(Guid userId, string notificationCode, bool isEnabled)
    {
        UserId = userId;
        NotificationCode = notificationCode;
        IsEnabled = isEnabled;
    }

    public void Enable() => IsEnabled = true;
    public void Disable() => IsEnabled = false;
}
