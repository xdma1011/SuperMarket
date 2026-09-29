using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Domain.Finance;

namespace SupermarketSystem.Application.Finance.GetExpenses;

public sealed record GetExpensesQuery(
    PagedRequest Paging, Guid? BranchId, int? PeriodYear, int? PeriodMonth, ExpenseCategory? Category, Guid? ExpenseTypeId = null);

public sealed record ExpenseListItemDto(
    Guid Id,
    Guid BranchId,
    ExpenseCategory Category,
    decimal Amount,
    DateTime PaymentDateUtc,
    int PeriodYear,
    int PeriodMonth,
    string? Notes,
    Guid RecordedByUserId,
    string RecordedByUsername)
{
    /// <summary>29/9/2026: النوع اللي بيعرّفه صاحب المحل + "من الصندوق" + الموظف لو المصروف راتب انصرف من صفحة الموظفين.</summary>
    public Guid? ExpenseTypeId { get; init; }
    public string ExpenseTypeName { get; init; } = "";
    public bool PaidFromDrawer { get; init; }
    public Guid? EmployeePaymentId { get; init; }
    public string? EmployeeName { get; init; }
}

/// <summary>
/// سجل تاريخي بحت (Expense بلا أي مسار تعديل/حذف بالـDomain) - نفس فلسفة
/// GetRecentReturns/CashDrawerLog تمامًا، بما فيها LEFT JOIN للمستخدم.
/// </summary>
public sealed class GetExpensesHandler
{
    private const string UnresolvedUserLabel = "(unresolved)";

    private readonly IApplicationDbContext _context;

    public GetExpensesHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<ExpenseListItemDto>> HandleAsync(GetExpensesQuery query, CancellationToken cancellationToken)
    {
        var paging = query.Paging.Normalized();

        var expenses = _context.Expenses.AsNoTracking().AsQueryable();

        if (query.BranchId is { } branchId)
        {
            expenses = expenses.Where(e => e.BranchId == branchId);
        }

        if (query.PeriodYear is { } year)
        {
            expenses = expenses.Where(e => e.PeriodYear == year);
        }

        if (query.PeriodMonth is { } month)
        {
            expenses = expenses.Where(e => e.PeriodMonth == month);
        }

        if (query.Category is { } category)
        {
            expenses = expenses.Where(e => e.Category == category);
        }

        if (query.ExpenseTypeId is { } typeId)
        {
            expenses = expenses.Where(e => e.ExpenseTypeId == typeId);
        }

        expenses = expenses.OrderByDescending(e => e.PeriodYear).ThenByDescending(e => e.PeriodMonth).ThenByDescending(e => e.PaymentDateUtc);

        var totalCount = await expenses.CountAsync(cancellationToken);

        var items = await expenses
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .GroupJoin(_context.Users.AsNoTracking(),
                e => e.RecordedByUserId,
                u => u.Id,
                (e, matchedUsers) => new { e, matchedUsers })
            .SelectMany(
                x => x.matchedUsers.DefaultIfEmpty(),
                (x, u) => new ExpenseListItemDto(
                    x.e.Id,
                    x.e.BranchId,
                    x.e.Category,
                    x.e.Amount,
                    x.e.PaymentDateUtc,
                    x.e.PeriodYear,
                    x.e.PeriodMonth,
                    x.e.Notes,
                    x.e.RecordedByUserId,
                    u != null ? u.Username : UnresolvedUserLabel))
            .ToListAsync(cancellationToken);

        // النوع والصندوق والموظف: استعلامات صغيرة بعد الصفحة (بلا تعقيد الـGroupJoin فوق).
        var ids = items.Select(i => i.Id).ToList();
        var extras = await _context.Expenses.AsNoTracking()
            .Where(e => ids.Contains(e.Id))
            .Select(e => new { e.Id, e.ExpenseTypeId, e.PaidFromDrawer, e.EmployeePaymentId })
            .ToDictionaryAsync(e => e.Id, cancellationToken);
        var typeNames = await _context.ExpenseTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name, cancellationToken);
        var paymentIds = extras.Values.Where(e => e.EmployeePaymentId != null).Select(e => e.EmployeePaymentId!.Value).ToList();
        var employeeNames = paymentIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await (from payment in _context.EmployeePayments.IgnoreQueryFilters().AsNoTracking()
                     join employee in _context.Employees.IgnoreQueryFilters().AsNoTracking() on payment.EmployeeId equals employee.Id
                     where paymentIds.Contains(payment.Id)
                     select new { payment.Id, employee.FullName })
                .ToDictionaryAsync(x => x.Id, x => x.FullName, cancellationToken);

        items = items.Select(i =>
        {
            var extra = extras[i.Id];
            return i with
            {
                ExpenseTypeId = extra.ExpenseTypeId,
                ExpenseTypeName = extra.ExpenseTypeId is { } t && typeNames.TryGetValue(t, out var name) ? name : "بلا نوع",
                PaidFromDrawer = extra.PaidFromDrawer,
                EmployeePaymentId = extra.EmployeePaymentId,
                EmployeeName = extra.EmployeePaymentId is { } p && employeeNames.TryGetValue(p, out var emp) ? emp : null
            };
        }).ToList();

        return new PagedResult<ExpenseListItemDto>(items, totalCount, paging.PageNumber, paging.PageSize);
    }
}
