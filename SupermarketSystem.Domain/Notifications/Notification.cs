using SupermarketSystem.Domain.Common;

namespace SupermarketSystem.Domain.Notifications;

public enum NotificationChannel
{
    InApp = 1,
    Email = 2,
    Sms = 3,
    Push = 4,
    Telegram = 5
}

public enum NotificationStatus
{
    Pending = 1,
    Sent = 2,
    Read = 3,
    Failed = 4
}

/// <summary>
/// Aggregate root.
///
/// TargetUserId is nullable — a deliberate change from the original Phase C
/// design (was a required Guid). Reason: several real trigger events (stock
/// went negative, cash-closing variance) don't have a single specific human
/// recipient without a real role/RBAC system (not built yet — D11).
/// Forcing a fake target (the same way User.SystemUserId stands in for an
/// unknown actor) would be dishonest here in a different way: an actor
/// field records "who did this" and SystemUserId honestly means "no real
/// user was authenticated" — but a recipient field forced to a specific
/// user would claim "this was meant for that person" when it wasn't. Null
/// means "broadcast — no specific recipient chosen yet", which is the
/// truthful state until role-based targeting exists.
///
/// NotificationLog is kept as a child (delivery-attempt log) rather than
/// merged into Notification, because the architecture anticipates retries
/// across channels for future external integrations (Architecture Review
/// §24) — legitimate observability for an external integration, not
/// redundant with Notification's own status.
/// </summary>
public class Notification : AuditableEntity
{
    public Guid? TargetUserId { get; private set; }
    public string Title { get; private set; } = null!;
    public string Message { get; private set; } = null!;
    public NotificationChannel Channel { get; private set; }
    public NotificationStatus Status { get; private set; }
    public DateTime? ReadAtUtc { get; private set; }

    private readonly List<NotificationLog> _deliveryAttempts = new();
    public IReadOnlyCollection<NotificationLog> DeliveryAttempts => _deliveryAttempts.AsReadOnly();

    private Notification() { } // EF Core

    public Notification(Guid? targetUserId, string title, string message, NotificationChannel channel)
    {
        TargetUserId = targetUserId;
        Title = title;
        Message = message;
        Channel = channel;
        Status = NotificationStatus.Pending;
    }

    public NotificationLog RecordDeliveryAttempt(DateTime attemptedAtUtc, bool success, string? errorMessage)
    {
        var log = new NotificationLog(Id, attemptedAtUtc, success, errorMessage);
        _deliveryAttempts.Add(log);
        Status = success ? NotificationStatus.Sent : NotificationStatus.Failed;
        return log;
    }

    public void MarkRead(DateTime readAtUtc)
    {
        Status = NotificationStatus.Read;
        ReadAtUtc = readAtUtc;
    }
}

/// <summary>Child of the Notification aggregate. Append-only delivery-attempt record.</summary>
public class NotificationLog : Entity
{
    public Guid NotificationId { get; private set; }
    public DateTime AttemptedAtUtc { get; private set; }
    public bool Success { get; private set; }
    public string? ErrorMessage { get; private set; }

    private NotificationLog() { } // EF Core

    internal NotificationLog(Guid notificationId, DateTime attemptedAtUtc, bool success, string? errorMessage)
    {
        NotificationId = notificationId;
        AttemptedAtUtc = attemptedAtUtc;
        Success = success;
        ErrorMessage = errorMessage;
    }
}
