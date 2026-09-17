using SupermarketSystem.Domain.Common;

namespace SupermarketSystem.Domain.Finance;

/// <summary>
/// راتب مُعتبَر نفس فئة الإيجار/الكهرباء/الماء تصميميًا (دفعة دورية
/// بتاريخ استحقاق) - التمييز بينهم بالتصنيف فقط، لا ببنية مختلفة
/// (طلب صاحب المشروع صراحة: "اعتبرهم موظف، لن نختلف عن المسميات").
/// </summary>
public enum ExpenseCategory
{
    Rent = 1,
    Electricity = 2,
    Water = 3,
    Salary = 4,
    Other = 5
}

/// <summary>
/// مصروف تشغيلي لفرع معيّن - سجل تاريخي بحت (بلا تعديل/حذف، نفس فلسفة
/// CashDrawerLog: أي تصحيح لاحق يصير بمصروف جديد لا بتعديل القديم).
///
/// PeriodYear/PeriodMonth منفصلان عمدًا عن PaymentDateUtc: مثال صاحب
/// المشروع بالذات - إيجار يُدفَع اليوم لشهر قادم، فالفترة (Period)
/// تكون الشهر القادم، لا شهر الدفع الفعلي - عشان كشف أرباح شهر الدفع
/// نفسه ما يتأثر بمصروف يخص شهر تاني.
/// </summary>
public class Expense : Entity, IBranchOwned
{
    public Guid BranchId { get; private set; }
    public ExpenseCategory Category { get; private set; }
    public decimal Amount { get; private set; }
    public DateTime PaymentDateUtc { get; private set; }
    public int PeriodYear { get; private set; }
    public int PeriodMonth { get; private set; }
    public string? Notes { get; private set; }
    public Guid RecordedByUserId { get; private set; }

    private Expense() { } // EF Core

    public Expense(
        Guid branchId, ExpenseCategory category, decimal amount, DateTime paymentDateUtc,
        int periodYear, int periodMonth, string? notes, Guid recordedByUserId)
    {
        if (amount <= 0)
        {
            throw new DomainException("Expense amount must be positive.");
        }

        if (periodMonth is < 1 or > 12)
        {
            throw new DomainException("Period month must be between 1 and 12.");
        }

        BranchId = branchId;
        Category = category;
        Amount = amount;
        PaymentDateUtc = paymentDateUtc;
        PeriodYear = periodYear;
        PeriodMonth = periodMonth;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        RecordedByUserId = recordedByUserId;
    }
}
