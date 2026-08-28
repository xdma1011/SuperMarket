namespace SupermarketSystem.Application.Common.Policies;

/// <summary>
/// The sensitive POS operations whose availability is administrator-
/// configurable. An enum (not a database table) because each member has code
/// behind it — a new member means new business logic, which means a deploy
/// anyway. The *answer* for each is configurable data; the *question* is not.
/// </summary>
public enum PosOperation
{
    CompleteSale = 1,
    VoidSale = 2,
    ProcessReturn = 3,
    /// <summary>A refund whose payment method differs from the original sale's — Architecture Review §16.10.</summary>
    CrossMethodRefund = 4,
    /// <summary>An ad-hoc discount keyed in at checkout (DiscountId IS NULL), as opposed to a configured Discount rule.</summary>
    ManualDiscount = 5,
    ReversePayment = 6
}

/// <summary>
/// Stable setting keys. Centralized here so no magic strings appear in
/// business logic (brief §36) and so the admin UI, the seeder and the policy
/// service can never drift apart on spelling.
/// </summary>
public static class PosPolicyKeys
{
    public const string AllowVoidSale = "Pos.AllowVoidSale";
    public const string AllowReturn = "Pos.AllowReturn";
    public const string AllowCrossMethodRefund = "Pos.AllowCrossMethodRefund";
    public const string AllowManualDiscount = "Pos.AllowManualDiscount";
    public const string AllowPaymentReversal = "Pos.AllowPaymentReversal";

    /// <summary>Ad-hoc discount ceiling, as a percentage of the line/invoice total. 0 disables manual discounts entirely.</summary>
    public const string MaxManualDiscountPercentage = "Pos.MaxManualDiscountPercentage";

    /// <summary>Above this amount a return is still ALLOWED, but is flagged as high-value for management review. Never blocks.</summary>
    public const string HighValueReturnThreshold = "Pos.HighValueReturnThreshold";
}

/// <summary>
/// The outcome of a policy check.
///
/// CRITICAL DESIGN POINT — there is deliberately NO "PendingApproval" state.
/// A decision is Allowed or Denied, returned in microseconds from cached
/// settings. The cashier is never parked waiting for a manager, and no
/// database row is ever written in a "waiting" state. This is the
/// "allow → complete → record → classify → review later" philosophy
/// expressed in code rather than in a comment.
///
/// RequiresReview does NOT block anything. It marks an operation that
/// completed normally but should surface in management review queues —
/// e.g. a high-value return, or a cash refund against a card sale.
/// </summary>
public sealed record PolicyDecision(bool IsAllowed, bool RequiresReview, string? Reason)
{
    public static PolicyDecision Allow() => new(true, false, null);

    /// <summary>Allowed and completed normally, but flagged for later management review.</summary>
    public static PolicyDecision AllowWithReview(string reason) => new(true, true, reason);

    /// <summary>Administrator has switched this operation off. An immediate, final answer — not a wait.</summary>
    public static PolicyDecision Deny(string reason) => new(false, false, reason);
}

/// <summary>
/// Answers "is this cashier allowed to do this right now?" instantly, from
/// cached settings. Implementations must never perform an uncached database
/// round-trip on the checkout hot path, and must never return a pending state.
/// </summary>
public interface IPosPolicyService
{
    Task<PolicyDecision> EvaluateAsync(PosOperation operation, CancellationToken cancellationToken);

    /// <summary>
    /// Amount-aware overload for operations whose policy depends on a value
    /// (manual discount ceiling, high-value return threshold).
    /// </summary>
    Task<PolicyDecision> EvaluateAsync(PosOperation operation, decimal amount, decimal? comparisonBase, CancellationToken cancellationToken);
}
