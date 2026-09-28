using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Domain.Finance;

namespace SupermarketSystem.Application.Finance.GetCapitalTransactions;

public sealed record GetCapitalTransactionsQuery(PagedRequest Paging, Guid? BranchId);

public sealed record CapitalTransactionListItemDto(
    Guid Id,
    Guid BranchId,
    CapitalTransactionType Type,
    decimal Amount,
    DateTime OccurredAtUtc,
    string? Notes,
    Guid RecordedByUserId,
    string RecordedByUsername,
    // الشريك صاحب الحركة (وحدة الشركاء، 28/9/2026) - null = حركة عامة للفرع.
    Guid? PartnerId = null,
    string? PartnerName = null);

/// <summary>سجل تاريخي بحت (CapitalTransaction بلا أي مسار تعديل/حذف) - نفس فلسفة GetExpenses تمامًا.</summary>
public sealed class GetCapitalTransactionsHandler
{
    private const string UnresolvedUserLabel = "(unresolved)";

    private readonly IApplicationDbContext _context;

    public GetCapitalTransactionsHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<CapitalTransactionListItemDto>> HandleAsync(
        GetCapitalTransactionsQuery query, CancellationToken cancellationToken)
    {
        var paging = query.Paging.Normalized();

        var transactions = _context.CapitalTransactions.AsNoTracking().AsQueryable();

        if (query.BranchId is { } branchId)
        {
            transactions = transactions.Where(c => c.BranchId == branchId);
        }

        transactions = transactions.OrderByDescending(c => c.OccurredAtUtc).ThenByDescending(c => c.Id);

        var totalCount = await transactions.CountAsync(cancellationToken);

        var items = await transactions
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .GroupJoin(_context.Users.AsNoTracking(),
                c => c.RecordedByUserId,
                u => u.Id,
                (c, matchedUsers) => new { c, matchedUsers })
            .SelectMany(
                x => x.matchedUsers.DefaultIfEmpty(),
                (x, u) => new CapitalTransactionListItemDto(
                    x.c.Id,
                    x.c.BranchId,
                    x.c.Type,
                    x.c.Amount,
                    x.c.OccurredAtUtc,
                    x.c.Notes,
                    x.c.RecordedByUserId,
                    u != null ? u.Username : UnresolvedUserLabel))
            .ToListAsync(cancellationToken);

        var withPartner = await transactions.Skip(paging.Skip).Take(paging.PageSize)
            .Where(c => c.PartnerId != null)
            .Select(c => new { c.Id, PartnerId = c.PartnerId!.Value })
            .ToListAsync(cancellationToken);
        if (withPartner.Count > 0)
        {
            var partnerIds = withPartner.Select(w => w.PartnerId).Distinct().ToList();
            var names = await _context.Partners.IgnoreQueryFilters().AsNoTracking()
                .Where(p => partnerIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.FullName, cancellationToken);
            var byTransaction = withPartner.ToDictionary(w => w.Id, w => w.PartnerId);
            items = items.Select(i => byTransaction.TryGetValue(i.Id, out var pid)
                ? i with { PartnerId = pid, PartnerName = names.GetValueOrDefault(pid) }
                : i).ToList();
        }

        return new PagedResult<CapitalTransactionListItemDto>(items, totalCount, paging.PageNumber, paging.PageSize);
    }
}
