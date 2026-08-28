using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Policies;

namespace SupermarketSystem.Infrastructure.Services;

/// <summary>
/// Resolves POS policy from cached settings. Every path here is an immediate
/// answer — there is no branch anywhere in this class that parks an operation
/// awaiting approval, because that state does not exist in this system.
///
/// DEFAULTS MATTER: each setting's default is the value used when an
/// administrator has never configured it. They are permissive for routine
/// operations (returns, voids, payment reversal) so a fresh install does not
/// accidentally block a working till, and restrictive only where the
/// operation creates a genuine reconciliation gap that someone must
/// consciously opt into (cross-method refund).
/// </summary>
public sealed class PosPolicyService : IPosPolicyService
{
    // Routine customer-facing operations: enabled unless switched off.
    private const bool DefaultAllowVoidSale = true;
    private const bool DefaultAllowReturn = true;
    private const bool DefaultAllowPaymentReversal = true;
    private const bool DefaultAllowManualDiscount = true;

    // A cash refund against a card/CliQ sale moves money out of the drawer
    // that never entered it. Allowed only when explicitly enabled.
    private const bool DefaultAllowCrossMethodRefund = false;

    private const decimal DefaultMaxManualDiscountPercentage = 10m;

    // 0 = no threshold configured = never flag on value alone.
    private const decimal DefaultHighValueReturnThreshold = 0m;

    private readonly ISettingsProvider _settings;

    public PosPolicyService(ISettingsProvider settings)
    {
        _settings = settings;
    }

    public Task<PolicyDecision> EvaluateAsync(PosOperation operation, CancellationToken cancellationToken)
        => EvaluateAsync(operation, amount: 0m, comparisonBase: null, cancellationToken);

    public async Task<PolicyDecision> EvaluateAsync(
        PosOperation operation,
        decimal amount,
        decimal? comparisonBase,
        CancellationToken cancellationToken)
    {
        return operation switch
        {
            // Completing a sale is the till's core function — it is never
            // policy-gated. Its real guards are structural (stock, payment
            // totals), not configurable.
            PosOperation.CompleteSale => PolicyDecision.Allow(),

            PosOperation.VoidSale => await EvaluateSimpleToggleAsync(
                PosPolicyKeys.AllowVoidSale,
                DefaultAllowVoidSale,
                "Voiding sales is disabled in system settings.",
                cancellationToken),

            PosOperation.ProcessReturn => await EvaluateReturnAsync(amount, cancellationToken),

            PosOperation.CrossMethodRefund => await EvaluateSimpleToggleAsync(
                PosPolicyKeys.AllowCrossMethodRefund,
                DefaultAllowCrossMethodRefund,
                "Refunding to a payment method other than the original is disabled in system settings.",
                cancellationToken,
                // Even when enabled, this always warrants a look: it is the
                // one combination that creates a drawer-vs-bank mismatch.
                reviewReasonWhenAllowed: "Refund issued to a different payment method than the original sale."),

            PosOperation.ManualDiscount => await EvaluateManualDiscountAsync(amount, comparisonBase, cancellationToken),

            PosOperation.ReversePayment => await EvaluateSimpleToggleAsync(
                PosPolicyKeys.AllowPaymentReversal,
                DefaultAllowPaymentReversal,
                "Payment reversal is disabled in system settings.",
                cancellationToken),

            _ => PolicyDecision.Deny($"Unknown POS operation '{operation}'.")
        };
    }

    private async Task<PolicyDecision> EvaluateSimpleToggleAsync(
        string settingKey,
        bool defaultValue,
        string denyReason,
        CancellationToken cancellationToken,
        string? reviewReasonWhenAllowed = null)
    {
        var allowed = await _settings.GetBoolAsync(settingKey, defaultValue, cancellationToken);

        if (!allowed)
        {
            return PolicyDecision.Deny(denyReason);
        }

        return reviewReasonWhenAllowed is null
            ? PolicyDecision.Allow()
            : PolicyDecision.AllowWithReview(reviewReasonWhenAllowed);
    }

    private async Task<PolicyDecision> EvaluateReturnAsync(decimal returnAmount, CancellationToken cancellationToken)
    {
        var allowed = await _settings.GetBoolAsync(PosPolicyKeys.AllowReturn, DefaultAllowReturn, cancellationToken);

        if (!allowed)
        {
            return PolicyDecision.Deny("Returns are disabled in system settings.");
        }

        var threshold = await _settings.GetDecimalAsync(
            PosPolicyKeys.HighValueReturnThreshold,
            DefaultHighValueReturnThreshold,
            cancellationToken);

        // Note the asymmetry: exceeding the threshold flags the return for
        // review, it does NOT block it. The cashier completes the return and
        // the customer leaves; the manager sees it afterwards.
        if (threshold > 0m && returnAmount > threshold)
        {
            return PolicyDecision.AllowWithReview(
                $"Return value {returnAmount} exceeds the high-value review threshold {threshold}.");
        }

        return PolicyDecision.Allow();
    }

    private async Task<PolicyDecision> EvaluateManualDiscountAsync(
        decimal discountAmount,
        decimal? lineTotal,
        CancellationToken cancellationToken)
    {
        var allowed = await _settings.GetBoolAsync(
            PosPolicyKeys.AllowManualDiscount,
            DefaultAllowManualDiscount,
            cancellationToken);

        if (!allowed)
        {
            return PolicyDecision.Deny("Manual discounts are disabled in system settings.");
        }

        var maxPercentage = await _settings.GetDecimalAsync(
            PosPolicyKeys.MaxManualDiscountPercentage,
            DefaultMaxManualDiscountPercentage,
            cancellationToken);

        if (maxPercentage <= 0m)
        {
            return PolicyDecision.Deny("Manual discounts are disabled (maximum permitted percentage is zero).");
        }

        // Without a base to compare against, the ceiling cannot be evaluated.
        // The discount is still allowed — but it is flagged, so an unbounded
        // discount never passes through completely unremarked.
        if (lineTotal is null or <= 0m)
        {
            return PolicyDecision.AllowWithReview("Manual discount applied without a comparable base amount.");
        }

        var requestedPercentage = discountAmount / lineTotal.Value * 100m;

        if (requestedPercentage > maxPercentage)
        {
            return PolicyDecision.Deny(
                $"Manual discount of {requestedPercentage:F2}% exceeds the maximum permitted {maxPercentage:F2}%.");
        }

        return PolicyDecision.Allow();
    }
}
