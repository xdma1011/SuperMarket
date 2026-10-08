using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Notifications;
using SupermarketSystem.Application.Partners;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.CashManagement;
using SupermarketSystem.Domain.Finance;
using SupermarketSystem.Domain.Identity;

namespace SupermarketSystem.Application.Finance.CreateExpense;

/// <summary>
/// PeriodYear/PeriodMonth منفصلان عمدًا عن PaymentDateUtc - راجع تعليق
/// Expense.cs بالـDomain (إيجار يُدفَع اليوم لشهر قادم، الفترة تخص الشهر
/// القادم لا شهر الدفع الفعلي).
///
/// 29/9/2026: ExpenseTypeId = نوع بيعرّفه صاحب المحل (تنظيف، صيانة...). طلب قديم بـCategory بس بيشتغل زي قبل
/// (بياخد النوع المبذور المقابل). PaidFromDrawer = انصرف من كاش الصندوق: بينكتب PayOut فبينقص المتوقع بالتقفيل
/// (بلا هيك المصروف الكاش بيطلع عجز وهمي بالتقفيل).
/// </summary>
public sealed record CreateExpenseCommand(
    Guid BranchId,
    ExpenseCategory? Category,
    decimal Amount,
    DateTime PaymentDateUtc,
    int PeriodYear,
    int PeriodMonth,
    string? Notes,
    Guid? ExpenseTypeId = null,
    bool PaidFromDrawer = false);

public sealed record CreateExpenseResponse(Guid ExpenseId);

public static class CreateExpenseValidator
{
    public static Error? Validate(CreateExpenseCommand command)
    {
        if (command.BranchId == Guid.Empty)
        {
            return Error.Validation("Expense.BranchRequired", "A branch is required.");
        }

        if (command.Amount <= 0)
        {
            return Error.Validation("Expense.AmountInvalid", "Expense amount must be positive.");
        }

        if (command.PeriodMonth is < 1 or > 12)
        {
            return Error.Validation("Expense.PeriodMonthInvalid", "Period month must be between 1 and 12.");
        }

        if (command.ExpenseTypeId is null && command.Category is null)
        {
            return Error.Validation("Expense.TypeRequired", "اختار نوع المصروف.");
        }

        if (command.ExpenseTypeId is null && command.Category is { } category && !Enum.IsDefined(category))
        {
            return Error.Validation("Expense.CategoryInvalid", "نوع المصروف مش صحيح.");
        }

        return null;
    }

    /// <summary>النوع المبذور المقابل للتصنيف القديم.</summary>
    public static Guid TypeIdForCategory(ExpenseCategory category) => category switch
    {
        ExpenseCategory.Rent => ExpenseType.RentId,
        ExpenseCategory.Electricity => ExpenseType.ElectricityId,
        ExpenseCategory.Water => ExpenseType.WaterId,
        ExpenseCategory.Salary => ExpenseType.SalaryId,
        _ => ExpenseType.OtherId
    };
}

public sealed class CreateExpenseHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly INotificationDispatcher _notificationDispatcher;

    public CreateExpenseHandler(
        IApplicationDbContext context, ICurrentUserContext currentUser, IDateTimeProvider dateTimeProvider,
        INotificationDispatcher notificationDispatcher)
    {
        _context = context;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
        _notificationDispatcher = notificationDispatcher;
    }

    public async Task<Result<CreateExpenseResponse>> HandleAsync(CreateExpenseCommand command, CancellationToken cancellationToken)
    {
        var validationError = CreateExpenseValidator.Validate(command);
        if (validationError is not null)
        {
            return Result.Failure<CreateExpenseResponse>(validationError);
        }

        var branchExists = await _context.Branches.AsNoTracking().AnyAsync(b => b.Id == command.BranchId, cancellationToken);
        if (!branchExists)
        {
            return Result.Failure<CreateExpenseResponse>(
                Error.NotFound("Expense.BranchNotFound", $"Branch '{command.BranchId}' was not found."));
        }

        var typeId = command.ExpenseTypeId ?? CreateExpenseValidator.TypeIdForCategory(command.Category!.Value);
        var type = await _context.ExpenseTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == typeId, cancellationToken);
        if (type is null)
        {
            return Result.Failure<CreateExpenseResponse>(Error.NotFound("Expense.TypeNotFound", "نوع المصروف مش موجود."));
        }

        // طلب قديم بـCategory بس لنوع صاحب المحل وقّفه = بيضل يشتغل (ما بنكسر مسار قديم)؛ اختيار صريح لنوع موقوف مرفوض.
        if (!type.IsActive && command.ExpenseTypeId is not null)
        {
            return Result.Failure<CreateExpenseResponse>(Error.BusinessRule("Expense.TypeInactive", $"نوع المصروف \"{type.Name}\" موقوف."));
        }

        var actorUserId = _currentUser.UserId ?? User.SystemUserId;

        Expense expense;
        try
        {
            expense = new Expense(
                command.BranchId, type.Id, type.CategoryForExpense, Math.Round(command.Amount, 3, MidpointRounding.AwayFromZero),
                command.PaymentDateUtc, command.PeriodYear, command.PeriodMonth, command.Notes, actorUserId, command.PaidFromDrawer);
        }
        catch (Domain.Common.DomainException ex)
        {
            return Result.Failure<CreateExpenseResponse>(Error.Validation("Expense.InvalidDetails", ex.Message));
        }

        _context.Expenses.Add(expense);

        if (command.PaidFromDrawer)
        {
            // وقت التسجيل (مش تاريخ الدفع المكتوب): التقفيل بيجمع الحركات بعد آخر تقفيل، فالكاش اللي طلع بينحسب
            // بأول تقفيل جاي. تاريخ الدفع بيضل على المصروف نفسه للكشف.
            _context.CashDrawerLogs.Add(new CashDrawerLog(
                command.BranchId, CashDrawerMovementType.PayOut, expense.Amount, CashDrawerReferenceType.Expense,
                expense.Id, actorUserId, _dateTimeProvider.UtcNow));
        }

        await _context.SaveChangesAsync(cancellationToken);

        // مصروف بفترة شهر نزل كشفه = الكشف صار قديم (بند 2 بالمراجعة النقدية).
        await PartnerStatementStaleness.MarkAndNotifyAsync(
            _context, _notificationDispatcher, _dateTimeProvider.UtcNow, command.BranchId,
            new[] { (command.PeriodYear, command.PeriodMonth) },
            $"انضاف مصروف ({expense.Amount:0.000} د.أ) بفترة {command.PeriodMonth}/{command.PeriodYear} بعد نزول الكشف.", cancellationToken);

        return Result.Success(new CreateExpenseResponse(expense.Id));
    }
}
