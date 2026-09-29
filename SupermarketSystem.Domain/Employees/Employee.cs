using SupermarketSystem.Domain.Common;

namespace SupermarketSystem.Domain.Employees;

/// <summary>
/// موظف بالمحل (29/9/2026) - للرواتب والسلف بس (مش حساب دخول: الدخول بـUser). لكل فرع لحاله. بلا حذف - موظف ترك
/// بيتوقف (IsActive=false) وسجل رواتبه بيضل.
/// </summary>
public class Employee : AuditableEntity, IBranchOwned
{
    public const int MaxNameLength = 150;

    public Guid BranchId { get; private set; }
    public string FullName { get; private set; } = null!;
    public string? Phone { get; private set; }
    public decimal MonthlySalary { get; private set; }
    public bool IsActive { get; private set; }
    public string? Notes { get; private set; }

    private Employee() { } // EF Core

    public Employee(Guid branchId, string fullName, string? phone, decimal monthlySalary, string? notes)
    {
        BranchId = branchId;
        Update(fullName, phone, monthlySalary, notes);
        IsActive = true;
    }

    public void Update(string fullName, string? phone, decimal monthlySalary, string? notes)
    {
        var name = fullName?.Trim() ?? string.Empty;
        if (name.Length == 0 || name.Length > MaxNameLength)
        {
            throw new DomainException($"Employee name must be 1-{MaxNameLength} characters.");
        }

        if (monthlySalary < 0)
        {
            throw new DomainException("Monthly salary cannot be negative.");
        }

        FullName = name;
        Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        MonthlySalary = monthlySalary;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    public void SetActive(bool isActive) => IsActive = isActive;
}

public enum EmployeePaymentType
{
    /// <summary>راتب شهر معيّن - بينحسب مصروف (رواتب) بكامله بكشف الربح، حتى لو انخصم منه سلفة.</summary>
    Salary = 1,

    /// <summary>سلفة - مش مصروف (دين على الموظف)، بتنخصم من راتب لاحق.</summary>
    Advance = 2
}

/// <summary>
/// صرف راتب أو سلفة لموظف - سجل تاريخي بحت (بلا تعديل/حذف، نفس فلسفة CashDrawerLog). ClientRequestId فريد (§3.2).
///   راتب: GrossAmount = الراتب المستحق للشهر (بينحسب مصروف)، AdvanceDeducted = سلفة انخصمت منه، NetPaid = اللي انصرف فعليًا.
///   سلفة: GrossAmount = NetPaid = مبلغ السلفة، بلا فترة.
/// </summary>
public class EmployeePayment : Entity, IBranchOwned
{
    public const int MaxNotesLength = 500;

    public Guid EmployeeId { get; private set; }
    public Guid BranchId { get; private set; }
    public EmployeePaymentType Type { get; private set; }
    public decimal GrossAmount { get; private set; }
    public decimal AdvanceDeducted { get; private set; }
    public decimal NetPaid { get; private set; }
    public int? PeriodYear { get; private set; }
    public int? PeriodMonth { get; private set; }
    public bool PaidFromDrawer { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    public string? Notes { get; private set; }
    public Guid RecordedByUserId { get; private set; }
    public Guid ClientRequestId { get; private set; }

    private EmployeePayment() { } // EF Core

    public static EmployeePayment Salary(
        Guid employeeId, Guid branchId, decimal grossAmount, decimal advanceDeducted, int periodYear, int periodMonth,
        bool paidFromDrawer, DateTime occurredAtUtc, string? notes, Guid recordedByUserId, Guid clientRequestId)
    {
        if (grossAmount <= 0)
        {
            throw new DomainException("Salary amount must be positive.");
        }

        if (advanceDeducted < 0 || advanceDeducted > grossAmount)
        {
            throw new DomainException("Advance deduction must be between zero and the salary amount.");
        }

        if (periodMonth is < 1 or > 12)
        {
            throw new DomainException("Period month must be between 1 and 12.");
        }

        return new EmployeePayment
        {
            EmployeeId = employeeId, BranchId = branchId, Type = EmployeePaymentType.Salary,
            GrossAmount = grossAmount, AdvanceDeducted = advanceDeducted, NetPaid = grossAmount - advanceDeducted,
            PeriodYear = periodYear, PeriodMonth = periodMonth, PaidFromDrawer = paidFromDrawer,
            OccurredAtUtc = occurredAtUtc, Notes = Clean(notes), RecordedByUserId = recordedByUserId, ClientRequestId = clientRequestId
        };
    }

    public static EmployeePayment Advance(
        Guid employeeId, Guid branchId, decimal amount, bool paidFromDrawer, DateTime occurredAtUtc, string? notes,
        Guid recordedByUserId, Guid clientRequestId)
    {
        if (amount <= 0)
        {
            throw new DomainException("Advance amount must be positive.");
        }

        return new EmployeePayment
        {
            EmployeeId = employeeId, BranchId = branchId, Type = EmployeePaymentType.Advance,
            GrossAmount = amount, AdvanceDeducted = 0m, NetPaid = amount, PaidFromDrawer = paidFromDrawer,
            OccurredAtUtc = occurredAtUtc, Notes = Clean(notes), RecordedByUserId = recordedByUserId, ClientRequestId = clientRequestId
        };
    }

    private static string? Clean(string? notes) =>
        string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()[..Math.Min(notes.Trim().Length, MaxNotesLength)];
}
