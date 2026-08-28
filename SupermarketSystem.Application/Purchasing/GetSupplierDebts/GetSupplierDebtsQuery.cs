using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Domain.Purchasing;

namespace SupermarketSystem.Application.Purchasing.GetSupplierDebts;

public sealed record SupplierDebtDto(
    Guid SupplierId,
    string SupplierName,
    decimal TotalInvoiced,
    decimal TotalPaid,
    decimal RemainingDebt,
    int UnpaidInvoiceCount);

public sealed record GetSupplierDebtsResponse(
    IReadOnlyList<SupplierDebtDto> Suppliers, decimal GrandTotalDebt);

/// <summary>
/// بس فواتير Received تُحسب — فاتورة Draft لسه ما توصلت البضاعة فعليًا،
/// وCancelled ملغاة بالكامل. RemainingDebt = TotalAmount - TotalPaidAmount
/// لكل فاتورة، مجمّعة على مستوى المورد.
/// </summary>
public sealed class GetSupplierDebtsHandler
{
    private readonly IApplicationDbContext _context;

    public GetSupplierDebtsHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<GetSupplierDebtsResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var invoices = await _context.PurchaseInvoices.AsNoTracking()
            .Where(pi => pi.Status == PurchaseInvoiceStatus.Received)
            .Select(pi => new { pi.SupplierId, pi.TotalAmount, pi.TotalPaidAmount })
            .ToListAsync(cancellationToken);

        var supplierNames = await _context.Suppliers.AsNoTracking()
            .ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);

        var grouped = invoices
            .GroupBy(i => i.SupplierId)
            .Select(g => new SupplierDebtDto(
                g.Key,
                supplierNames.GetValueOrDefault(g.Key, "(غير معروف)"),
                g.Sum(i => i.TotalAmount),
                g.Sum(i => i.TotalPaidAmount),
                g.Sum(i => i.TotalAmount - i.TotalPaidAmount),
                g.Count(i => i.TotalAmount > i.TotalPaidAmount)))
            .Where(s => s.RemainingDebt > 0)
            .OrderByDescending(s => s.RemainingDebt)
            .ToList();

        return new GetSupplierDebtsResponse(grouped, grouped.Sum(s => s.RemainingDebt));
    }
}
