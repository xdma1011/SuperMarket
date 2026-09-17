using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Domain.Finance;

namespace SupermarketSystem.Application.Finance.GetExpenses;

public sealed record GetExpensesQuery(
    PagedRequest Paging, Guid? BranchId, int? PeriodYear, int? PeriodMonth, ExpenseCategory? Category);

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
    string RecordedByUsername);

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

        return new PagedResult<ExpenseListItemDto>(items, totalCount, paging.PageNumber, paging.PageSize);
    }
}
