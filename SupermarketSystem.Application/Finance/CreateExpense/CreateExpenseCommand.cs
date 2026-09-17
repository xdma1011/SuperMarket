using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Finance;
using SupermarketSystem.Domain.Identity;

namespace SupermarketSystem.Application.Finance.CreateExpense;

/// <summary>
/// PeriodYear/PeriodMonth منفصلان عمدًا عن PaymentDateUtc - راجع تعليق
/// Expense.cs بالـDomain (إيجار يُدفَع اليوم لشهر قادم، الفترة تخص الشهر
/// القادم لا شهر الدفع الفعلي).
/// </summary>
public sealed record CreateExpenseCommand(
    Guid BranchId,
    ExpenseCategory Category,
    decimal Amount,
    DateTime PaymentDateUtc,
    int PeriodYear,
    int PeriodMonth,
    string? Notes);

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

        return null;
    }
}

public sealed class CreateExpenseHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;

    public CreateExpenseHandler(IApplicationDbContext context, ICurrentUserContext currentUser)
    {
        _context = context;
        _currentUser = currentUser;
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

        var actorUserId = _currentUser.UserId ?? User.SystemUserId;

        Expense expense;
        try
        {
            expense = new Expense(
                command.BranchId, command.Category, command.Amount, command.PaymentDateUtc,
                command.PeriodYear, command.PeriodMonth, command.Notes, actorUserId);
        }
        catch (Domain.Common.DomainException ex)
        {
            return Result.Failure<CreateExpenseResponse>(Error.Validation("Expense.InvalidDetails", ex.Message));
        }

        _context.Expenses.Add(expense);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(new CreateExpenseResponse(expense.Id));
    }
}
