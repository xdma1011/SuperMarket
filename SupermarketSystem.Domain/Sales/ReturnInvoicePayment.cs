using SupermarketSystem.Domain.Common;
using SupermarketSystem.Domain.Payments;

namespace SupermarketSystem.Domain.Sales;

/// <summary>
/// Child of the ReturnInvoice aggregate. Mirrors SaleInvoicePayment exactly
/// — replaces the flat "RefundAmount on ReturnInvoice" design that couldn't
/// support split refunds, an audit trail, or reconciliation (Architecture
/// Review §16.4). Refund-method policy (same method vs. a different one
/// than the original sale) is NOT enforced here as a hard rule — see
/// §16.10: allowed, not blocked, flagged via a query-time comparison
/// instead of a stored field.
/// </summary>
public class ReturnInvoicePayment : Entity, IBranchOwned, IHasRowVersion
{
    public Guid ReturnInvoiceId { get; private set; }
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

    private ReturnInvoicePayment() { } // EF Core

    internal ReturnInvoicePayment(Guid returnInvoiceId, Guid paymentMethodId, decimal amount, Guid userId, Guid branchId, string? externalReference, Guid clientRequestId)
    {
        if (amount <= 0)
        {
            throw new DomainException("Refund amount must be positive.");
        }

        ReturnInvoiceId = returnInvoiceId;
        PaymentMethodId = paymentMethodId;
        Amount = amount;
        UserId = userId;
        BranchId = branchId;
        CreatedAtUtc = DateTime.UtcNow;
        Status = PaymentStatus.Completed;
        ExternalReference = externalReference;
        ClientRequestId = clientRequestId;
    }

    public void Reverse(Guid reversedByUserId, DateTime reversedAtUtc, string reason)
    {
        if (Status != PaymentStatus.Completed)
        {
            throw new DomainException("Only a Completed refund can be reversed.");
        }

        Status = PaymentStatus.Reversed;
        ReversedAtUtc = reversedAtUtc;
        ReversedByUserId = reversedByUserId;
        ReversedReason = reason;
    }

    public void MarkFailed() => Status = PaymentStatus.Failed;
}
