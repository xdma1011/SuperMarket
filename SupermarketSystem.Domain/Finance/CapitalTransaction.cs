using SupermarketSystem.Domain.Common;

namespace SupermarketSystem.Domain.Finance;

public enum CapitalTransactionType
{
    Deposit = 1,
    Withdrawal = 2
}

/// <summary>
/// حركة رأس مال يدوية صريحة (سحب/إضافة) - منفصلة كليًا عن كشف الأرباح
/// الشهري (GetMonthlyProfitStatement). لا ربط تلقائي بينهم: الربح رقم
/// إعلامي (كيف كان الشهر)، رأس المال رقم تراكمي ما يتحرّك إلا بفعل
/// إداري صريح - طلب صاحب المشروع بالذات (15-17/9/2026).
///
/// صلاحية Finance.Manage فقط (Master Admin افتراضيًا) - لا تُمنح
/// لمساعد أدمن بشكل افتراضي، عمدًا.
/// </summary>
public class CapitalTransaction : Entity, IBranchOwned
{
    public Guid BranchId { get; private set; }
    public CapitalTransactionType Type { get; private set; }
    public decimal Amount { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    public string? Notes { get; private set; }
    public Guid RecordedByUserId { get; private set; }

    private CapitalTransaction() { } // EF Core

    public CapitalTransaction(
        Guid branchId, CapitalTransactionType type, decimal amount, DateTime occurredAtUtc,
        string? notes, Guid recordedByUserId)
    {
        if (amount <= 0)
        {
            throw new DomainException("Capital transaction amount must be positive; direction is expressed by Type, not sign.");
        }

        BranchId = branchId;
        Type = type;
        Amount = amount;
        OccurredAtUtc = occurredAtUtc;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        RecordedByUserId = recordedByUserId;
    }
}
