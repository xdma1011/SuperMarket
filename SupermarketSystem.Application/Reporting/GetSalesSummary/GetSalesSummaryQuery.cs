using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Domain.Sales;

namespace SupermarketSystem.Application.Reporting.GetSalesSummary;

public sealed record GetSalesSummaryQuery(
    Guid? BranchId,
    DateTime FromUtc,
    DateTime ToUtc,
    // فترة مقارنة اختيارية — لو انعطت، الرد بيرجع بلوك تاني بنفس الشكل
    // لهاي الفترة، بلا استعلام منفصل يطلبه المستخدم.
    DateTime? CompareFromUtc,
    DateTime? CompareToUtc);

public sealed record SalesSummaryPeriodDto(
    DateTime FromUtc,
    DateTime ToUtc,
    int InvoiceCount,
    // مبيعات الفواتير الملغاة لا تُحسب — الإلغاء يعني الفاتورة لم تحدث فعليًا.
    decimal TotalSales,
    decimal TotalDiscounts,
    decimal TotalReturnedAmount,
    // صافي الإيراد = المبيعات - المرتجعات. لا يشمل هامش الربح (يحتاج طريقة
    // حساب تكلفة غير مبنية بعد — قرار معلّق، غير مطروق هون عمدًا).
    decimal NetRevenue);

public sealed record GetSalesSummaryResponse(
    SalesSummaryPeriodDto Period,
    SalesSummaryPeriodDto? ComparisonPeriod,
    // نسبة التغيّر بصافي الإيراد بين الفترتين، null لو ما في فترة مقارنة
    // أو لو الفترة المرجعية كانت صفر (تفادي القسمة على صفر).
    decimal? NetRevenueChangePercent);

/// <summary>
/// تقرير مبيعات لفترة واحدة، مع إمكانية مقارنتها بفترة تانية بنفس الطلب —
/// هذا يغطي "يومي/أسبوعي/شهري" (الفترة تُحدَّد من المستدعي، من/لتاريخ)
/// و"مقارنة الفترات" (المعامِلات الاختيارية) بنفس الاستعلام، بلا حاجة
/// لـendpoint منفصل لكل حالة.
/// </summary>
public sealed class GetSalesSummaryHandler
{
    private readonly IApplicationDbContext _context;

    public GetSalesSummaryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<GetSalesSummaryResponse> HandleAsync(GetSalesSummaryQuery query, CancellationToken cancellationToken)
    {
        var period = await ComputePeriodAsync(query.BranchId, query.FromUtc, query.ToUtc, cancellationToken);

        SalesSummaryPeriodDto? comparisonPeriod = null;
        if (query.CompareFromUtc is { } compareFrom && query.CompareToUtc is { } compareTo)
        {
            comparisonPeriod = await ComputePeriodAsync(query.BranchId, compareFrom, compareTo, cancellationToken);
        }

        decimal? changePercent = null;
        if (comparisonPeriod is not null && comparisonPeriod.NetRevenue != 0)
        {
            changePercent = (period.NetRevenue - comparisonPeriod.NetRevenue) / comparisonPeriod.NetRevenue * 100m;
        }

        return new GetSalesSummaryResponse(period, comparisonPeriod, changePercent);
    }

    private async Task<SalesSummaryPeriodDto> ComputePeriodAsync(
        Guid? branchId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken)
    {
        var invoices = _context.SaleInvoices.AsNoTracking()
            .Where(s => s.Status != SaleInvoiceStatus.Voided
                        && s.CreatedAtUtc >= fromUtc && s.CreatedAtUtc <= toUtc);

        if (branchId is { } id)
        {
            invoices = invoices.Where(s => s.BranchId == id);
        }

        // تجميع واحد بدل أربع استعلامات منفصلة — أسرع وأوضح.
        var aggregate = await invoices
            .GroupBy(_ => 1)
            .Select(g => new
            {
                InvoiceCount = g.Count(),
                TotalSales = g.Sum(s => s.TotalAmount),
                TotalDiscounts = g.Sum(s => s.DiscountAmountSnapshot),
                TotalReturnedAmount = g.Sum(s => s.TotalReturnedAmount)
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (aggregate is null)
        {
            return new SalesSummaryPeriodDto(fromUtc, toUtc, 0, 0m, 0m, 0m, 0m);
        }

        return new SalesSummaryPeriodDto(
            fromUtc, toUtc,
            aggregate.InvoiceCount,
            aggregate.TotalSales,
            aggregate.TotalDiscounts,
            aggregate.TotalReturnedAmount,
            aggregate.TotalSales - aggregate.TotalReturnedAmount);
    }
}
