using SupermarketSystem.Domain.Common;
using SupermarketSystem.Domain.Payments;

namespace SupermarketSystem.Domain.Sales;

/// <summary>
/// Child of the SaleInvoice aggregate (Cascade delete from SaleInvoice —
/// true aggregate-internal child, same treatment as SaleInvoiceItem).
/// Immutable once created except for the single Completed -> Reversed
/// transition (Architecture Review §16.3/§16.5): Amount, PaymentMethodId,
/// ExternalReference, UserId, CreatedAtUtc never change. BranchId is
/// deliberately denormalized from SaleInvoice.BranchId (never diverges,
/// since that's itself immutable) purely so branch-scoped payment reports
/// don't need to join back to SaleInvoice.
/// ClientRequestId is the idempotency key — a client-generated id, unique
/// indexed, so a retried submission (network failure, double-click) is
/// detected rather than creating a duplicate payment (§16.12 scenario 4/9).
/// </summary>
public class SaleInvoicePayment : Entity, IBranchOwned, IHasRowVersion
{
    public Guid SaleInvoiceId { get; private set; }
    public Guid PaymentMethodId { get; private set; }
    public decimal Amount { get; private set; }
    public Guid UserId { get; private set; }
    public Guid BranchId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public PaymentStatus Status { get; private set; }
    public string? ExternalReference { get; private set; }
    public Guid ClientRequestId { get; private set; }

    public DateTime? ReversedAtUtc { get; private set; }
    public Guid? ReversedByUserId { get; private set; }
    public string? ReversedReason { get; private set; }

    public byte[]? RowVersion { get; private set; }

    private SaleInvoicePayment() { } // EF Core

    internal SaleInvoicePayment(Guid saleInvoiceId, Guid paymentMethodId, decimal amount, Guid userId, Guid branchId, string? externalReference, Guid clientRequestId)
    {
        if (amount <= 0)
        {
            throw new DomainException("Payment amount must be positive.");
        }

        SaleInvoiceId = saleInvoiceId;
        PaymentMethodId = paymentMethodId;
        Amount = amount;
        UserId = userId;
        BranchId = branchId;
        CreatedAtUtc = DateTime.UtcNow;
        Status = PaymentStatus.Completed;
        ExternalReference = externalReference;
        ClientRequestId = clientRequestId;
    }

    /// <summary>
    /// The only post-completion transition. Reversing a cash payment is
    /// what drives the compensating CashDrawerLog entry
    /// (MovementType.PaymentReversalCashOut) — see §16.6. Used both for
    /// voiding a sale (reverse each of its payments) and for correcting a
    /// misrecorded payment (reverse + record a new one), unified into one
    /// mechanism.
    /// </summary>
    public void Reverse(Guid reversedByUserId, DateTime reversedAtUtc, string reason)
    {
        if (Status != PaymentStatus.Completed)
        {
            throw new DomainException("Only a Completed payment can be reversed.");
        }

        Status = PaymentStatus.Reversed;
        ReversedAtUtc = reversedAtUtc;
        ReversedByUserId = reversedByUserId;
        ReversedReason = reason;
    }

    public void MarkFailed() => Status = PaymentStatus.Failed;
}
