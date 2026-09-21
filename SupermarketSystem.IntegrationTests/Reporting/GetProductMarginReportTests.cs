using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Application.Purchasing.CompletePurchaseInvoice;
using SupermarketSystem.Application.Reporting.GetProductMarginReport;
using SupermarketSystem.Application.Sales.CompleteSale;
using SupermarketSystem.Application.Sales.ProcessReturn;
using SupermarketSystem.Domain.Sales;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Reporting;

/// <summary>
/// هامش الربح لكل منتج - أول استخدام لـUnitCostSnapshot على مستوى المنتج
/// لا الفرع/الشهر الإجمالي بس. راجع تعليق GetProductMarginReportQuery.cs.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class GetProductMarginReportTests : IntegrationTestBase
{
    public GetProductMarginReportTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task هامش_الربح_لكل_منتج_يحسب_الإيراد_والتكلفة_بدقة()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);

        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج هامش ربح");
        await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, sellingPrice: 10m);
        var supplier = await TestDataBuilder.CreateSupplierAsync(db);

        var purchaseHandler = scope.ServiceProvider.GetRequiredService<CompletePurchaseInvoiceHandler>();
        var purchase = await purchaseHandler.HandleAsync(
            new CompletePurchaseInvoiceCommand(
                Fixture.TestBranchId, supplier.Id, null,
                new[] { new CompletePurchaseInvoiceItemDto(product.Id, unit.Id, 20m, 4m, null, null, null) }),
            CancellationToken.None);
        Assert.True(purchase.IsSuccess);

        var saleHandler = scope.ServiceProvider.GetRequiredService<CompleteSaleHandler>();
        var sale = await saleHandler.HandleAsync(
            new CompleteSaleCommand(
                Fixture.TestBranchId, Guid.NewGuid(), null, 0m,
                new[] { new CompleteSaleItemDto(product.Id, unit.Id, 5m, 0m, null) },
                new[] { new CompleteSalePaymentDto(TestDataBuilder.CashPaymentMethodId, 50m, null, Guid.NewGuid()) }),
            CancellationToken.None);
        Assert.True(sale.IsSuccess);

        var now = DateTime.UtcNow;
        var reportHandler = scope.ServiceProvider.GetRequiredService<GetProductMarginReportHandler>();
        var report = await reportHandler.HandleAsync(
            new GetProductMarginReportQuery(new PagedRequest(), Fixture.TestBranchId, now.AddMinutes(-5), now.AddMinutes(5)),
            CancellationToken.None);

        var item = Assert.Single(report.Items.Items);
        Assert.Equal(product.Id, item.ProductId);
        Assert.Equal(5m, item.QuantitySold);
        Assert.Equal(50m, item.NetRevenue);
        Assert.Equal(20m, item.Cost); // 5 × 4
        Assert.Equal(30m, item.Margin);
        Assert.Equal(60m, item.MarginPercent); // 30 / 50 × 100
        Assert.Equal(0, item.LinesExcludedNoCostHistory);
        Assert.Equal(50m, report.TotalNetRevenue);
        Assert.Equal(20m, report.TotalCost);
        Assert.Equal(30m, report.TotalMargin);
    }

    [Fact]
    public async Task إرجاع_جزئي_يقلّل_الإيراد_والتكلفة_معًا_بنسبة_الكمية_المرتجعة()
    {
        Guid productId;
        Guid saleInvoiceId;
        Guid saleItemId;

        // Scope منفصلة للشراء+البيع، تُغلق قبل الإرجاع - راجع تعليق
        // ProcessReturnTests.CompleteASaleAsync: تفادي RowVersion زائف
        // الفشل بسبب خصم المخزون الذري عبر SQL خام بنفس الـScope المتتبِّع.
        using (var setupScope = CreateScope())
        {
            await TestDataBuilder.ActAsAdminAsync(setupScope, Fixture);
            var db = CreateDbContext(setupScope);

            var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج إرجاع هامش");
            await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, sellingPrice: 10m);
            var supplier = await TestDataBuilder.CreateSupplierAsync(db);
            productId = product.Id;

            var purchaseHandler = setupScope.ServiceProvider.GetRequiredService<CompletePurchaseInvoiceHandler>();
            await purchaseHandler.HandleAsync(
                new CompletePurchaseInvoiceCommand(
                    Fixture.TestBranchId, supplier.Id, null,
                    new[] { new CompletePurchaseInvoiceItemDto(product.Id, unit.Id, 20m, 4m, null, null, null) }),
                CancellationToken.None);

            var saleHandler = setupScope.ServiceProvider.GetRequiredService<CompleteSaleHandler>();
            var sale = await saleHandler.HandleAsync(
                new CompleteSaleCommand(
                    Fixture.TestBranchId, Guid.NewGuid(), null, 0m,
                    new[] { new CompleteSaleItemDto(product.Id, unit.Id, 10m, 0m, null) },
                    new[] { new CompleteSalePaymentDto(TestDataBuilder.CashPaymentMethodId, 100m, null, Guid.NewGuid()) }),
                CancellationToken.None);
            Assert.True(sale.IsSuccess);
            saleInvoiceId = sale.Value.SaleInvoiceId;

            saleItemId = await db.SaleInvoiceItems.AsNoTracking()
                .Where(i => i.SaleInvoiceId == saleInvoiceId).Select(i => i.Id).FirstAsync();
        }

        using var returnScope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(returnScope, Fixture);
        var returnHandler = returnScope.ServiceProvider.GetRequiredService<ProcessReturnHandler>();
        var returnResult = await returnHandler.HandleAsync(
            new ProcessReturnCommand(
                saleInvoiceId, Guid.NewGuid(), ReturnReason.CustomerChangedMind, null,
                new[] { new ProcessReturnItemDto(saleItemId, 4m) },
                new[] { new ProcessReturnPaymentDto(TestDataBuilder.CashPaymentMethodId, 40m, null, Guid.NewGuid()) }),
            CancellationToken.None);
        Assert.True(returnResult.IsSuccess);

        var now = DateTime.UtcNow;
        var reportHandler = returnScope.ServiceProvider.GetRequiredService<GetProductMarginReportHandler>();
        var report = await reportHandler.HandleAsync(
            new GetProductMarginReportQuery(new PagedRequest(), Fixture.TestBranchId, now.AddMinutes(-5), now.AddMinutes(5)),
            CancellationToken.None);

        var item = Assert.Single(report.Items.Items);
        Assert.Equal(productId, item.ProductId);
        Assert.Equal(6m, item.QuantitySold); // 10 - 4
        Assert.Equal(60m, item.NetRevenue); // 100 - 4×10
        Assert.Equal(24m, item.Cost); // 6 × 4
        Assert.Equal(36m, item.Margin);
    }

    [Fact]
    public async Task منتج_بلا_تاريخ_شراء_يُستبعد_من_التكلفة_ويعلَّم_بعدد_الأسطر()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);

        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج هامش بلا تاريخ شراء");
        await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, sellingPrice: 10m);
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 20m);

        var saleHandler = scope.ServiceProvider.GetRequiredService<CompleteSaleHandler>();
        var sale = await saleHandler.HandleAsync(
            new CompleteSaleCommand(
                Fixture.TestBranchId, Guid.NewGuid(), null, 0m,
                new[] { new CompleteSaleItemDto(product.Id, unit.Id, 3m, 0m, null) },
                new[] { new CompleteSalePaymentDto(TestDataBuilder.CashPaymentMethodId, 30m, null, Guid.NewGuid()) }),
            CancellationToken.None);
        Assert.True(sale.IsSuccess);

        var now = DateTime.UtcNow;
        var reportHandler = scope.ServiceProvider.GetRequiredService<GetProductMarginReportHandler>();
        var report = await reportHandler.HandleAsync(
            new GetProductMarginReportQuery(new PagedRequest(), Fixture.TestBranchId, now.AddMinutes(-5), now.AddMinutes(5)),
            CancellationToken.None);

        var item = Assert.Single(report.Items.Items);
        Assert.Equal(0m, item.Cost);
        Assert.Equal(30m, item.NetRevenue);
        Assert.Equal(30m, item.Margin);
        Assert.Equal(1, item.LinesExcludedNoCostHistory);
    }
}
