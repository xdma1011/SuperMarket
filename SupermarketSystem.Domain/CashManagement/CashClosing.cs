using SupermarketSystem.Domain.Common;

namespace SupermarketSystem.Domain.CashManagement;

/// <summary>
/// Aggregate root, branch-owned. The expected-vs-counted-cash reconciliation
/// record for a shift/day — the backstop that catches whatever a
/// payment-level guard couldn't (Architecture Review §16.12 scenario 12).
/// Finalized/immutable once created — reconciled against CashDrawerLog
/// entries since the last closing, which is a read-only aggregation
/// (CashDrawerLog itself is never mutated).
/// </summary>
public class CashClosing : AuditableEntity, IBranchOwned, IHasRowVersion
{
    public Guid BranchId { get; private set; }
    public Guid UserId { get; private set; }

    /// <summary>
    /// The branch's business day/shift this closing covers — supplied
    /// explicitly by the caller (who knows the branch's shift boundaries),
    /// not derived from ClosedAtUtc. This, not ClosedAtUtc, is what "one
    /// closing per branch per business day" is actually enforced against
    /// (see the unique index in Infrastructure) — an exact timestamp can
    /// never collide with another, so it cannot express that rule.
    /// </summary>
    public DateOnly BusinessDate { get; private set; }

    public DateTime ClosedAtUtc { get; private set; }
    public decimal ExpectedCash { get; private set; }
    public decimal CountedCash { get; private set; }
    public decimal Variance => CountedCash - ExpectedCash;
    public byte[]? RowVersion { get; private set; }

    private readonly List<CashClosingDetail> _details = new();
    public IReadOnlyCollection<CashClosingDetail> Details => _details.AsReadOnly();

    private CashClosing() { } // EF Core

    public CashClosing(Guid branchId, Guid userId, DateOnly businessDate, DateTime closedAtUtc, decimal expectedCash, decimal countedCash)
    {
        BranchId = branchId;
        UserId = userId;
        BusinessDate = businessDate;
        ClosedAtUtc = closedAtUtc;
        ExpectedCash = expectedCash;
        CountedCash = countedCash;
    }

    public CashClosingDetail AddDetail(Guid paymentMethodId, decimal expectedAmount, decimal? countedAmount)
    {
        var detail = new CashClosingDetail(Id, paymentMethodId, expectedAmount, countedAmount);
        _details.Add(detail);
        return detail;
    }
}

/// <summary>
/// Child of the CashClosing aggregate — breakdown by payment method.
/// CountedAmount is meaningful for Cash (physical count); for Visa/CliQ it
/// is left null in Phase 1 (no terminal/bank integration yet) and populated
/// later once reconciliation exists (Architecture Review §16.15).
/// </summary>
public class CashClosingDetail : Entity
{
    public Guid CashClosingId { get; private set; }
    public Guid PaymentMethodId { get; private set; }
    public decimal ExpectedAmount { get; private set; }
    public decimal? CountedAmount { get; private set; }

    private CashClosingDetail() { } // EF Core

    internal CashClosingDetail(Guid cashClosingId, Guid paymentMethodId, decimal expectedAmount, decimal? countedAmount)
    {
        CashClosingId = cashClosingId;
        PaymentMethodId = paymentMethodId;
        ExpectedAmount = expectedAmount;
        CountedAmount = countedAmount;
    }
}
