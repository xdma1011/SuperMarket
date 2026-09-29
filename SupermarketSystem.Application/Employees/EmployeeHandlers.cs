using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.CashManagement;
using SupermarketSystem.Domain.Common;
using SupermarketSystem.Domain.Employees;
using SupermarketSystem.Domain.Finance;
using SupermarketSystem.Domain.Identity;

namespace SupermarketSystem.Application.Employees;

// =====================================================================================
// الموظفين والرواتب والسلف (29/9/2026، صاحب المشروع: "كيف بدي اعطي راتب الموظف عندي؟").
//   - موظف = اسم + راتب شهري لكل فرع (مش حساب دخول - الدخول بـUser).
//   - راتب شهر: بينسجّل **مصروف "رواتب" بكامل الراتب** (GrossAmount) بكشف ربح شهر الراتب، حتى لو انخصم منه سلفة -
//     السلفة كانت مصاري طلعت من قبل بس مش مصروف (دين على الموظف)، ولما تنخصم بتصير جزء من الراتب.
//     المدفوع فعليًا = الراتب − السلفة المخصومة (NetPaid).
//   - سلفة: مش مصروف بالكشف (دين على الموظف)، بتنخصم من راتب جاي. رصيد السلف = مجموع السلف − مجموع المخصوم.
//   - من الصندوق: PayOut بالمبلغ اللي طلع فعليًا (NetPaid) - فالتقفيل ما بيطلع عجز وهمي. "من برّا" = بلا أثر عالصندوق.
//   - سجل تاريخي بحت (بلا تعديل/حذف) + ClientRequestId (§3.2).
// =====================================================================================

public sealed record EmployeeDto(
    Guid Id, Guid BranchId, string FullName, string? Phone, decimal MonthlySalary, bool IsActive, string? Notes,
    decimal OutstandingAdvance, decimal TotalSalariesPaid, decimal TotalAdvancesGiven,
    int? LastSalaryYear, int? LastSalaryMonth);

public sealed record GetEmployeesQuery(Guid? BranchId, bool IncludeInactive);

public sealed record CreateEmployeeCommand(Guid BranchId, string FullName, string? Phone, decimal MonthlySalary, string? Notes);

public sealed record UpdateEmployeeCommand(Guid EmployeeId, string FullName, string? Phone, decimal MonthlySalary, string? Notes, bool IsActive);

/// <summary>
/// Type=Salary: Amount = راتب الشهر (قبل خصم السلفة)، AdvanceDeducted = سلفة بتنخصم منه، PeriodYear/Month إلزاميين.
/// Type=Advance: Amount = مبلغ السلفة (AdvanceDeducted والفترة بيتجاهلوا).
/// </summary>
public sealed record RecordEmployeePaymentCommand(
    Guid EmployeeId, EmployeePaymentType Type, decimal Amount, decimal AdvanceDeducted, int? PeriodYear, int? PeriodMonth,
    bool PaidFromDrawer, DateTime? OccurredAtUtc, string? Notes, Guid ClientRequestId);

public sealed record RecordEmployeePaymentResponse(
    Guid PaymentId, Guid EmployeeId, int TypeCode, string TypeTitle, decimal GrossAmount, decimal AdvanceDeducted, decimal NetPaid,
    decimal OutstandingAdvance, Guid? ExpenseId, decimal PaidBeforeForPeriod, bool WasReplay);

public sealed record EmployeePaymentDto(
    Guid Id, Guid EmployeeId, string EmployeeName, Guid BranchId, int TypeCode, string TypeTitle,
    decimal GrossAmount, decimal AdvanceDeducted, decimal NetPaid, int? PeriodYear, int? PeriodMonth,
    bool PaidFromDrawer, DateTime OccurredAtUtc, string? Notes, string RecordedByName);

public sealed record GetEmployeePaymentsQuery(Guid? BranchId, Guid? EmployeeId, int? Year);

public static class EmployeeTitles
{
    public static string PaymentType(EmployeePaymentType type) => type switch
    {
        EmployeePaymentType.Salary => "راتب",
        EmployeePaymentType.Advance => "سلفة",
        _ => type.ToString()
    };
}

internal static class EmployeeBalances
{
    public sealed record Totals(decimal Advances, decimal Deducted, decimal Salaries, int? LastYear, int? LastMonth)
    {
        public decimal OutstandingAdvance => Advances - Deducted;
    }

    public static async Task<Dictionary<Guid, Totals>> LoadAsync(IApplicationDbContext context, IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken)
    {
        var rows = await context.EmployeePayments.IgnoreQueryFilters().AsNoTracking()
            .Where(p => employeeIds.Contains(p.EmployeeId))
            .Select(p => new { p.EmployeeId, p.Type, p.GrossAmount, p.AdvanceDeducted, p.PeriodYear, p.PeriodMonth })
            .ToListAsync(cancellationToken);

        return employeeIds.ToDictionary(id => id, id =>
        {
            var mine = rows.Where(r => r.EmployeeId == id).ToList();
            var salaries = mine.Where(r => r.Type == EmployeePaymentType.Salary).ToList();
            var last = salaries.OrderByDescending(r => r.PeriodYear).ThenByDescending(r => r.PeriodMonth).FirstOrDefault();
            return new Totals(
                mine.Where(r => r.Type == EmployeePaymentType.Advance).Sum(r => r.GrossAmount),
                salaries.Sum(r => r.AdvanceDeducted),
                salaries.Sum(r => r.GrossAmount),
                last?.PeriodYear, last?.PeriodMonth);
        });
    }
}

public sealed class GetEmployeesHandler
{
    private readonly IApplicationDbContext _context;

    public GetEmployeesHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<EmployeeDto>> HandleAsync(GetEmployeesQuery query, CancellationToken cancellationToken)
    {
        var employeesQuery = _context.Employees.AsNoTracking();
        if (query.BranchId is { } branchId)
        {
            employeesQuery = employeesQuery.Where(e => e.BranchId == branchId);
        }

        if (!query.IncludeInactive)
        {
            employeesQuery = employeesQuery.Where(e => e.IsActive);
        }

        var employees = await employeesQuery.OrderByDescending(e => e.IsActive).ThenBy(e => e.FullName).ToListAsync(cancellationToken);
        var totals = await EmployeeBalances.LoadAsync(_context, employees.Select(e => e.Id).ToList(), cancellationToken);

        return employees.Select(e =>
        {
            var t = totals[e.Id];
            return new EmployeeDto(e.Id, e.BranchId, e.FullName, e.Phone, e.MonthlySalary, e.IsActive, e.Notes,
                t.OutstandingAdvance, t.Salaries, t.Advances, t.LastYear, t.LastMonth);
        }).ToList();
    }
}

public sealed class CreateEmployeeHandler
{
    private readonly IApplicationDbContext _context;

    public CreateEmployeeHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<Guid>> HandleAsync(CreateEmployeeCommand command, CancellationToken cancellationToken)
    {
        if (!await _context.Branches.AsNoTracking().AnyAsync(b => b.Id == command.BranchId, cancellationToken))
        {
            return Result.Failure<Guid>(Error.NotFound("Employee.BranchNotFound", "الفرع مش موجود."));
        }

        Employee employee;
        try
        {
            employee = new Employee(command.BranchId, command.FullName, command.Phone, Math.Round(command.MonthlySalary, 3, MidpointRounding.AwayFromZero), command.Notes);
        }
        catch (DomainException)
        {
            return Result.Failure<Guid>(Error.Validation("Employee.Invalid", "اكتب اسم الموظف (لحد 150 حرف)، والراتب ما بيكون سالب."));
        }

        _context.Employees.Add(employee);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(employee.Id);
    }
}

public sealed class UpdateEmployeeHandler
{
    private readonly IApplicationDbContext _context;

    public UpdateEmployeeHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result> HandleAsync(UpdateEmployeeCommand command, CancellationToken cancellationToken)
    {
        var employee = await _context.Employees.FirstOrDefaultAsync(e => e.Id == command.EmployeeId, cancellationToken);
        if (employee is null)
        {
            return Result.Failure(Error.NotFound("Employee.NotFound", "الموظف مش موجود."));
        }

        try
        {
            employee.Update(command.FullName, command.Phone, Math.Round(command.MonthlySalary, 3, MidpointRounding.AwayFromZero), command.Notes);
        }
        catch (DomainException)
        {
            return Result.Failure(Error.Validation("Employee.Invalid", "اكتب اسم الموظف (لحد 150 حرف)، والراتب ما بيكون سالب."));
        }

        employee.SetActive(command.IsActive);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed class RecordEmployeePaymentHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;

    public RecordEmployeePaymentHandler(IApplicationDbContext context, ICurrentUserContext currentUser, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<RecordEmployeePaymentResponse>> HandleAsync(RecordEmployeePaymentCommand command, CancellationToken cancellationToken)
    {
        if (command.ClientRequestId == Guid.Empty)
        {
            return Result.Failure<RecordEmployeePaymentResponse>(Error.Validation("EmployeePayment.ClientRequestIdRequired", "A client request id is required."));
        }

        var replay = await _context.EmployeePayments.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(p => p.ClientRequestId == command.ClientRequestId, cancellationToken);
        if (replay is not null)
        {
            var replayTotals = await EmployeeBalances.LoadAsync(_context, new[] { replay.EmployeeId }, cancellationToken);
            var replayExpenseId = await _context.Expenses.IgnoreQueryFilters().AsNoTracking()
                .Where(e => e.EmployeePaymentId == replay.Id).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(cancellationToken);
            return Result.Success(new RecordEmployeePaymentResponse(
                replay.Id, replay.EmployeeId, (int)replay.Type, EmployeeTitles.PaymentType(replay.Type), replay.GrossAmount,
                replay.AdvanceDeducted, replay.NetPaid, replayTotals[replay.EmployeeId].OutstandingAdvance, replayExpenseId, 0m, WasReplay: true));
        }

        if (!Enum.IsDefined(command.Type))
        {
            return Result.Failure<RecordEmployeePaymentResponse>(Error.Validation("EmployeePayment.TypeInvalid", "نوع الصرف مش صحيح."));
        }

        if (command.Notes is { Length: > EmployeePayment.MaxNotesLength })
        {
            return Result.Failure<RecordEmployeePaymentResponse>(Error.Validation("EmployeePayment.NotesTooLong", "الملاحظة طويلة كتير."));
        }

        var employee = await _context.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == command.EmployeeId, cancellationToken);
        if (employee is null)
        {
            return Result.Failure<RecordEmployeePaymentResponse>(Error.NotFound("Employee.NotFound", "الموظف مش موجود."));
        }

        // راتب آخر شهر لموظف ترك مسموح؛ سلفة لموظف موقوف لأ.
        if (command.Type == EmployeePaymentType.Advance && !employee.IsActive)
        {
            return Result.Failure<RecordEmployeePaymentResponse>(Error.BusinessRule("EmployeePayment.EmployeeInactive", "الموظف موقوف - ما في سلفة لموظف موقوف."));
        }

        var amount = Math.Round(command.Amount, 3, MidpointRounding.AwayFromZero);
        var deducted = command.Type == EmployeePaymentType.Salary ? Math.Round(command.AdvanceDeducted, 3, MidpointRounding.AwayFromZero) : 0m;

        var totals = (await EmployeeBalances.LoadAsync(_context, new[] { employee.Id }, cancellationToken))[employee.Id];
        if (deducted > totals.OutstandingAdvance)
        {
            return Result.Failure<RecordEmployeePaymentResponse>(Error.BusinessRule("EmployeePayment.DeductionExceedsAdvance",
                $"الخصم ({deducted:0.000}) أكتر من السلف المتبقية على الموظف ({totals.OutstandingAdvance:0.000})."));
        }

        var now = _dateTimeProvider.UtcNow;
        var occurredAt = command.OccurredAtUtc is { } requested && requested <= now ? DateTime.SpecifyKind(requested, DateTimeKind.Utc) : now;
        var actor = _currentUser.UserId ?? User.SystemUserId;

        EmployeePayment payment;
        try
        {
            if (command.Type == EmployeePaymentType.Salary)
            {
                if (command.PeriodYear is null || command.PeriodMonth is null)
                {
                    return Result.Failure<RecordEmployeePaymentResponse>(Error.Validation("EmployeePayment.PeriodRequired", "اختار شهر الراتب."));
                }

                payment = EmployeePayment.Salary(employee.Id, employee.BranchId, amount, deducted, command.PeriodYear.Value, command.PeriodMonth.Value,
                    command.PaidFromDrawer, occurredAt, command.Notes, actor, command.ClientRequestId);
            }
            else
            {
                payment = EmployeePayment.Advance(employee.Id, employee.BranchId, amount, command.PaidFromDrawer, occurredAt, command.Notes, actor, command.ClientRequestId);
            }
        }
        catch (DomainException ex)
        {
            return Result.Failure<RecordEmployeePaymentResponse>(Error.Validation("EmployeePayment.Invalid", ex.Message switch
            {
                var m when m.Contains("positive") => "المبلغ لازم يكون أكبر من صفر.",
                var m when m.Contains("deduction") => "خصم السلفة لازم يكون بين صفر والراتب.",
                var m when m.Contains("month") => "شهر الراتب مش صحيح.",
                var m => m
            }));
        }

        // سماح مع تنبيه بالواجهة (§1.6): راتب تاني لنفس الشهر مسموح (دفعة على دفعتين، مكافأة) - بنرجّع اللي انصرف قبل.
        var paidBefore = 0m;
        if (payment.Type == EmployeePaymentType.Salary)
        {
            paidBefore = await _context.EmployeePayments.AsNoTracking()
                .Where(p => p.EmployeeId == employee.Id && p.Type == EmployeePaymentType.Salary
                            && p.PeriodYear == payment.PeriodYear && p.PeriodMonth == payment.PeriodMonth)
                .SumAsync(p => (decimal?)p.GrossAmount, cancellationToken) ?? 0m;
        }

        _context.EmployeePayments.Add(payment);

        Guid? expenseId = null;
        if (payment.Type == EmployeePaymentType.Salary)
        {
            var expense = new Expense(
                employee.BranchId, ExpenseType.SalaryId, ExpenseCategory.Salary, payment.GrossAmount, occurredAt,
                payment.PeriodYear!.Value, payment.PeriodMonth!.Value,
                $"راتب {employee.FullName} - {payment.PeriodMonth:00}/{payment.PeriodYear}" +
                (payment.AdvanceDeducted > 0 ? $" (انخصم سلفة {payment.AdvanceDeducted:0.000})" : "") +
                (payment.Notes is null ? "" : $" - {payment.Notes}"),
                actor, payment.PaidFromDrawer, payment.Id);
            _context.Expenses.Add(expense);
            expenseId = expense.Id;
        }

        if (payment.PaidFromDrawer && payment.NetPaid > 0)
        {
            // وقت التسجيل (زي المصروف): التقفيل بيجمع الحركات بعد آخر تقفيل.
            _context.CashDrawerLogs.Add(new CashDrawerLog(
                employee.BranchId, CashDrawerMovementType.PayOut, payment.NetPaid, CashDrawerReferenceType.EmployeePayment, payment.Id, actor, now));
        }

        await _context.SaveChangesAsync(cancellationToken);

        var outstanding = payment.Type == EmployeePaymentType.Advance
            ? totals.OutstandingAdvance + payment.GrossAmount
            : totals.OutstandingAdvance - payment.AdvanceDeducted;

        return Result.Success(new RecordEmployeePaymentResponse(
            payment.Id, employee.Id, (int)payment.Type, EmployeeTitles.PaymentType(payment.Type), payment.GrossAmount,
            payment.AdvanceDeducted, payment.NetPaid, outstanding, expenseId, paidBefore, WasReplay: false));
    }
}

public sealed class GetEmployeePaymentsHandler
{
    private readonly IApplicationDbContext _context;

    public GetEmployeePaymentsHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<EmployeePaymentDto>> HandleAsync(GetEmployeePaymentsQuery query, CancellationToken cancellationToken)
    {
        var payments = _context.EmployeePayments.AsNoTracking();
        if (query.BranchId is { } branchId)
        {
            payments = payments.Where(p => p.BranchId == branchId);
        }

        if (query.EmployeeId is { } employeeId)
        {
            payments = payments.Where(p => p.EmployeeId == employeeId);
        }

        if (query.Year is { } year)
        {
            var from = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var to = from.AddYears(1);
            payments = payments.Where(p => (p.PeriodYear == year) || (p.PeriodYear == null && p.OccurredAtUtc >= from && p.OccurredAtUtc < to));
        }

        var rows = await (
                from p in payments
                join e in _context.Employees.IgnoreQueryFilters().AsNoTracking() on p.EmployeeId equals e.Id
                join u in _context.Users.IgnoreQueryFilters().AsNoTracking() on p.RecordedByUserId equals u.Id into users
                from u in users.DefaultIfEmpty()
                orderby p.OccurredAtUtc descending
                select new { p, e.FullName, RecordedBy = u != null ? u.FullName : null })
            .Take(1000)
            .ToListAsync(cancellationToken);

        return rows.Select(r => new EmployeePaymentDto(
                r.p.Id, r.p.EmployeeId, r.FullName, r.p.BranchId, (int)r.p.Type, EmployeeTitles.PaymentType(r.p.Type),
                r.p.GrossAmount, r.p.AdvanceDeducted, r.p.NetPaid, r.p.PeriodYear, r.p.PeriodMonth,
                r.p.PaidFromDrawer, r.p.OccurredAtUtc, r.p.Notes, r.RecordedBy ?? "غير معروف"))
            .ToList();
    }
}
