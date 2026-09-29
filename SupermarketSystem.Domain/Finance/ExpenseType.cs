using SupermarketSystem.Domain.Common;

namespace SupermarketSystem.Domain.Finance;

/// <summary>
/// نوع مصروف بيعرّفه صاحب المحل بنفسه (29/9/2026: "خليني انا اعرف نوع المصاريف") - تنظيف، صيانة، نقل... على مستوى
/// الشركة (مش لكل فرع). بلا حذف: نوع إله مصاريف بيتوقف (IsActive=false) عشان ما يختفي من التقارير القديمة.
/// ستة أنواع مبذورة (إيجار، كهرباء، ماء، رواتب، تنظيف، أخرى) - LegacyCategory بيربطها بـExpenseCategory القديم.
/// </summary>
public class ExpenseType : Entity
{
    public const int MaxNameLength = 100;

    public static readonly Guid RentId = Guid.Parse("e1a0b1c2-0001-4a5b-9c6d-7e8f90a1b201");
    public static readonly Guid ElectricityId = Guid.Parse("e1a0b1c2-0002-4a5b-9c6d-7e8f90a1b202");
    public static readonly Guid WaterId = Guid.Parse("e1a0b1c2-0003-4a5b-9c6d-7e8f90a1b203");
    public static readonly Guid SalaryId = Guid.Parse("e1a0b1c2-0004-4a5b-9c6d-7e8f90a1b204");
    public static readonly Guid OtherId = Guid.Parse("e1a0b1c2-0005-4a5b-9c6d-7e8f90a1b205");
    public static readonly Guid CleaningId = Guid.Parse("e1a0b1c2-0006-4a5b-9c6d-7e8f90a1b206");

    public string Name { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public int SortOrder { get; private set; }

    /// <summary>التصنيف القديم المقابل (للأنواع المبذورة)؛ نوع جديد = null (بيتخزّن على المصروف كـOther).</summary>
    public ExpenseCategory? LegacyCategory { get; private set; }

    private ExpenseType() { } // EF Core

    public ExpenseType(string name, int sortOrder)
    {
        Rename(name);
        SortOrder = sortOrder;
        IsActive = true;
    }

    public void Rename(string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > MaxNameLength)
        {
            throw new DomainException($"Expense type name must be 1-{MaxNameLength} characters.");
        }

        Name = trimmed;
    }

    public void SetActive(bool isActive) => IsActive = isActive;

    public void SetSortOrder(int sortOrder) => SortOrder = sortOrder;

    /// <summary>التصنيف اللي بينحفظ على المصروف (عمود Category القديم) - للتوافق مع أي شي لسه بيقرأه.</summary>
    public ExpenseCategory CategoryForExpense => LegacyCategory ?? ExpenseCategory.Other;
}
