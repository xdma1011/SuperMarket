using SupermarketSystem.Domain.Common;

namespace SupermarketSystem.Domain.CashManagement;

public enum CashDrawerMovementType
{
    SaleCashIn = 1,
    ReturnCashOut = 2,
    /// <summary>A completed cash SALE payment reversed — covers both void-triggered and correction-triggered reversal. See §16.6.</summary>
    PaymentReversalCashOut = 3,
    /// <summary>A completed cash REFUND payment reversed (rarer, symmetric case).</summary>
    PaymentReversalCashIn = 4,
    PayIn = 5,
    PayOut = 6,
    DrawerOpen = 7,
    DrawerClose = 8
}

/// <summary>
/// Which specific transaction a CashDrawerLog row traces to. Points at the
/// individual payment, not the invoice header (Architecture Review §16.6 —
/// this is what makes SaleInvoice -> SaleInvoicePayment -> CashDrawerLog a
/// precise, auditable chain rather than stopping at the invoice level).
/// </summary>
public enum CashDrawerReferenceType
{
    SaleInvoicePayment = 1,
    ReturnInvoicePayment = 2,
    ManualAdjustment = 3,

    /// <summary>
    /// The physical cash removed from the drawer at closing (MovementType
    /// = DrawerClose). Added because closing needed a way to point back at
    /// the CashClosing record it belongs to — this member did not exist
    /// before there was an operation that needed it.
    /// </summary>
    CashClosing = 4
}

/// <summary>
/// Aggregate root, branch-owned, historical/append-only. No update path at
/// all after insert — it has no Status, no lifecycle, it's a pure fact
/// record (§16.6). ReferenceType/ReferenceId is the same loose-reference
/// pattern as StockMovement, for the same reason (no clean single-table FK
/// target) and the same accepted trade-off (§8): only trusted internal
/// application services write here.
/// </summary>
public class CashDrawerLog : Entity, IBranchOwned
{
    public Guid BranchId { get; private set; }
    public CashDrawerMovementType MovementType { get; private set; }
    public decimal Amount { get; private set; }
    public CashDrawerReferenceType ReferenceType { get; private set; }
    public Guid ReferenceId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }

    private CashDrawerLog() { } // EF Core

    public CashDrawerLog(Guid branchId, CashDrawerMovementType movementType, decimal amount, CashDrawerReferenceType referenceType, Guid referenceId, Guid userId, DateTime occurredAtUtc)
    {
        if (amount <= 0)
        {
            throw new DomainException("CashDrawerLog amount must be positive; direction is expressed by MovementType, not sign.");
        }

        BranchId = branchId;
        MovementType = movementType;
        Amount = amount;
        ReferenceType = referenceType;
        ReferenceId = referenceId;
        UserId = userId;
        OccurredAtUtc = occurredAtUtc;
    }
}
