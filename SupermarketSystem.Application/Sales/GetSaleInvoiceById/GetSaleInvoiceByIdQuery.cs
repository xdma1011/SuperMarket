using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Sales;

namespace SupermarketSystem.Application.Sales.GetSaleInvoiceById;

public sealed record GetSaleInvoiceByIdQuery(Guid SaleInvoiceId);

public sealed record SaleInvoiceItemDetailDto(
    Guid SaleInvoiceItemId,
    Guid ProductId,
    string ProductName,
    decimal Quantity,
    decimal QuantityReturned,
    decimal UnitPriceSnapshot,
    decimal LineTotal);

public sealed record SaleInvoiceDetailDto(
    Guid Id,
    string InvoiceNumber,
    int StatusCode,
    string StatusTitle,
    decimal TotalAmount,
    decimal TotalReturnedAmount,
    DateTime CreatedAtUtc,
    IReadOnlyList<SaleInvoiceItemDetailDto> Items);

/// <summary>
/// أساس شاشة الإرجاع بالكامل — Quantity minus QuantityReturned هي
/// "الكمية القابلة للإرجاع" لكل سطر (الحارس الذري الحقيقي بالباك إند
/// بـProcessReturnHandler، هذا بس عرض للمستخدم قبل ما يقرر شو يرجّع).
/// </summary>
public sealed class GetSaleInvoiceByIdHandler
{
    private readonly IApplicationDbContext _context;

    public GetSaleInvoiceByIdHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<SaleInvoiceDetailDto>> HandleAsync(GetSaleInvoiceByIdQuery query, CancellationToken cancellationToken)
    {
        var invoice = await _context.SaleInvoices.AsNoTracking()
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.Id == query.SaleInvoiceId, cancellationToken);

        if (invoice is null)
        {
            return Result.Failure<SaleInvoiceDetailDto>(
                Error.NotFound("Sale.NotFound", $"الفاتورة '{query.SaleInvoiceId}' غير موجودة."));
        }

        var productIds = invoice.Items.Select(i => i.ProductId).Distinct().ToList();
        var productNames = await _context.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);

        var items = invoice.Items
            .Select(i => new SaleInvoiceItemDetailDto(
                i.Id, i.ProductId, productNames.GetValueOrDefault(i.ProductId, "(غير معروف)"),
                i.Quantity, i.QuantityReturned, i.UnitPriceSnapshot, i.LineTotal))
            .ToList();

        var dto = new SaleInvoiceDetailDto(
            invoice.Id, invoice.InvoiceNumber, (int)invoice.Status, StatusTitle(invoice.Status),
            invoice.TotalAmount, invoice.TotalReturnedAmount, invoice.CreatedAtUtc, items);

        return Result.Success(dto);
    }

    private static string StatusTitle(SaleInvoiceStatus status) => status switch
    {
        SaleInvoiceStatus.Completed => "مكتملة",
        SaleInvoiceStatus.Voided => "ملغاة",
        SaleInvoiceStatus.PartiallyReturned => "إرجاع جزئي",
        SaleInvoiceStatus.FullyReturned => "إرجاع كامل",
        _ => status.ToString()
    };
}
