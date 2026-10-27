using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Common.Costing;
using SupermarketSystem.Application.Finance.GetMonthlyProfitStatement;
using SupermarketSystem.Application.Inventory.RecordWasteIssue;
using SupermarketSystem.Application.Sales.CompleteSale;
using SupermarketSystem.Application.System.GetAdminSettings;
using SupermarketSystem.Application.System.UpdateAdminSetting;
using SupermarketSystem.Domain.Catalog;
using SupermarketSystem.Domain.Inventory;
using SupermarketSystem.Domain.Purchasing;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Finance;

/// <summary>
/// التكلفة التقديرية (27/10/2026، قرار صاحب المشروع): صنف بلا تكلفة معروفة = سعر البيع × (1 − هامش الإعدادات، افتراضي 15%) ومعلَّمة،
/// بدل الاستبعاد من الربح. الهامش قابل للتعديل من صفحة الإعدادات، 0 = تعطيل، ويتجمّد بالبيعة وقت البيع.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class EstimatedCostTests : IntegrationTestBase
{
    public EstimatedCostTests(DatabaseFixture fixture) : base(fixture) { }

    private async Task<Guid> SellAsync(IServiceScope scope, Product product, ProductUnit unit, decimal quantity, decimal paid)
    {
        var saleHandler = scope.ServiceProvider.GetRequiredService<CompleteSaleHandler>();
        var result = await saleHandler.HandleAsync(new CompleteSaleCommand(
            Fixture.TestBranchId, Guid.NewGuid(), null, 0m,
            new[] { new CompleteSaleItemDto(product.Id, unit.Id, quantity, 0m, null) },
            new[] { new CompleteSalePaymentDto(TestDataBuilder.CashPaymentMethodId, paid, null, Guid.NewGuid()) }),
            CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error?.Message);
        return result.Value.SaleInvoiceId;
    }

    private async Task<GetMonthlyProfitStatementResponse> StatementAsync(IServiceScope scope)
    {
        var now = DateTime.UtcNow;
        var statement = await scope.ServiceProvider.GetRequiredService<GetMonthlyProfitStatementHandler>().HandleAsync(
            new GetMonthlyProfitStatementQuery(Fixture.TestBranchId, now.Year, now.Month), CancellationToken.None);
        Assert.True(statement.IsSuccess);
        return statement.Value;
    }

    [Fact]
    public async Task بيع_صنف_بلا_تكلفة_بيتكلّف_85_بالمية_من_سعره_ومعلَّم_وبينحسب_بالربح()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "صنف بلا تكلفة - تقدير");
        await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, sellingPrice: 5m);
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 100m);

        var invoiceId = await SellAsync(scope, product, unit, quantity: 2m, paid: 10m);

        var item = await db.SaleInvoiceItems.AsNoTracking().FirstAsync(i => i.SaleInvoiceId == invoiceId);
        Assert.Equal(4.25m, item.UnitCostSnapshot);
        Assert.True(item.IsCostEstimated);

        var statement = await StatementAsync(scope);
        Assert.Equal(8.5m, statement.CostOfGoodsSold);
        Assert.Equal(1.5m, statement.GrossProfit);
        Assert.Equal(0, statement.ItemsExcludedNoCostHistory);
        Assert.Equal(1, statement.EstimatedCostItemsCount);
    }

    [Fact]
    public async Task صنف_إله_تكلفة_فعلية_ما_بيتقدّر_ولا_بيتعلّم()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "صنف إله تكلفة فعلية");
        await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, sellingPrice: 5m);
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 100m);
        var supplier = await TestDataBuilder.CreateSupplierAsync(db);
        var invoice = new PurchaseInvoice(Fixture.TestBranchId, supplier.Id, "PI-EST-REAL", null);
        invoice.AddItem(product.Id, unit.Id, null, 100m, 3m);
        invoice.MarkReceived();
        db.PurchaseInvoices.Add(invoice);
        await db.SaveChangesAsync();

        var invoiceId = await SellAsync(scope, product, unit, quantity: 2m, paid: 10m);

        var item = await db.SaleInvoiceItems.AsNoTracking().FirstAsync(i => i.SaleInvoiceId == invoiceId);
        Assert.Equal(3m, item.UnitCostSnapshot);
        Assert.False(item.IsCostEstimated);
        Assert.Equal(0, (await StatementAsync(scope)).EstimatedCostItemsCount);
    }

    [Fact]
    public async Task تغيير_الهامش_من_الإعدادات_بيأثر_على_البيعات_الجديدة_بس_والقديمة_متجمّدة()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "صنف تغيير الهامش");
        await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, sellingPrice: 10m);
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 100m);

        var first = await SellAsync(scope, product, unit, 1m, 10m); // 15% -> 8.5

        var update = await scope.ServiceProvider.GetRequiredService<UpdateAdminSettingHandler>().HandleAsync(
            new UpdateAdminSettingCommand(CostingSettingsKeys.EstimatedMarginPercent, "30"), CancellationToken.None);
        Assert.True(update.IsSuccess);

        var second = await SellAsync(scope, product, unit, 1m, 10m); // 30% -> 7

        var firstItem = await db.SaleInvoiceItems.AsNoTracking().FirstAsync(i => i.SaleInvoiceId == first);
        var secondItem = await db.SaleInvoiceItems.AsNoTracking().FirstAsync(i => i.SaleInvoiceId == second);
        Assert.Equal(8.5m, firstItem.UnitCostSnapshot);
        Assert.Equal(7m, secondItem.UnitCostSnapshot);
    }

    [Fact]
    public async Task بيع_بالكرتونة_تكلفته_التقديرية_بالوحدة_الأساسية_ضرب_المعامل()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var category = await TestDataBuilder.CreateCategoryAsync(db);
        var product = new Product("صنف كرتونة بلا تكلفة", category.Id, isBatchTracked: false);
        product.ChangeStatus(ProductStatus.Active);
        product.AddUnit("حبة", conversionFactorToBase: 1m, isBaseUnit: true);
        var carton = product.AddUnit("كرتونة", conversionFactorToBase: 12m, isBaseUnit: false);
        db.Products.Add(product);
        await db.SaveChangesAsync();
        await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, sellingPrice: 1m);
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 100m);

        var invoiceId = await SellAsync(scope, product, carton, quantity: 1m, paid: 12m);

        var item = await db.SaleInvoiceItems.AsNoTracking().FirstAsync(i => i.SaleInvoiceId == invoiceId);
        Assert.Equal(12m * 0.85m, item.UnitCostSnapshot); // حبة 1.000 × 0.85 × 12
        Assert.True(item.IsCostEstimated);
    }

    [Fact]
    public async Task التلف_لصنف_بلا_تكلفة_بينحسب_بتكلفة_تقديرية_ويتعد_بالتحذير()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "صنف تلف بلا تكلفة");
        await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, sellingPrice: 10m);
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 20m);

        var waste = await scope.ServiceProvider.GetRequiredService<RecordWasteIssueHandler>().HandleAsync(
            new RecordWasteIssueCommand(product.Id, unit.Id, Fixture.TestBranchId, 2m, WasteReason.Broken, null),
            CancellationToken.None);
        Assert.True(waste.IsSuccess);

        var statement = await StatementAsync(scope);
        Assert.Equal(17m, statement.WasteLossValue); // 2 × 10 × 0.85
        Assert.Equal(0, statement.WasteMovementsExcludedNoCostHistory);
        Assert.Equal(1, statement.EstimatedCostItemsCount);
    }

    [Fact]
    public async Task هامش_صفر_بيعطّل_التقدير_والصنف_بيتستبعد_زي_قبل()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        await TestDataBuilder.SetSettingAsync(scope, CostingSettingsKeys.EstimatedMarginPercent, 0m);
        var db = CreateDbContext(scope);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "صنف تقدير معطّل");
        await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, sellingPrice: 5m);
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 100m);

        var invoiceId = await SellAsync(scope, product, unit, 1m, 5m);

        var item = await db.SaleInvoiceItems.AsNoTracking().FirstAsync(i => i.SaleInvoiceId == invoiceId);
        Assert.Null(item.UnitCostSnapshot);
        Assert.False(item.IsCostEstimated);
        var statement = await StatementAsync(scope);
        Assert.Equal(1, statement.ItemsExcludedNoCostHistory);
        Assert.Equal(0, statement.EstimatedCostItemsCount);
    }

    [Theory]
    [InlineData("-5")]
    [InlineData("95")]
    [InlineData("100")]
    [InlineData("abc")]
    public async Task الإعدادات_بترفض_هامش_خارج_المدى_أو_غير_رقمي(string value)
    {
        using var scope = CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<UpdateAdminSettingHandler>().HandleAsync(
            new UpdateAdminSettingCommand(CostingSettingsKeys.EstimatedMarginPercent, value), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("AdminSettings.InvalidValue", result.Error!.Code);
    }

    [Fact]
    public async Task الهامش_بيظهر_بصفحة_الإعدادات_وبينقبل_التعديل()
    {
        using var scope = CreateScope();
        var update = await scope.ServiceProvider.GetRequiredService<UpdateAdminSettingHandler>().HandleAsync(
            new UpdateAdminSettingCommand(CostingSettingsKeys.EstimatedMarginPercent, "20"), CancellationToken.None);
        Assert.True(update.IsSuccess);

        var settings = await scope.ServiceProvider.GetRequiredService<GetAdminSettingsHandler>().HandleAsync(CancellationToken.None);
        var item = settings.Settings.Single(s => s.Key == CostingSettingsKeys.EstimatedMarginPercent);
        Assert.Equal("20", item.Value);
        Assert.Equal(AdminSettingDataType.Decimal, item.DataType);
    }

    [Fact]
    public async Task صفحة_الإعدادات_بتعرض_الافتراضي_15_قبل_أي_حفظ()
    {
        using var scope = CreateScope();
        var settings = await scope.ServiceProvider.GetRequiredService<GetAdminSettingsHandler>().HandleAsync(CancellationToken.None);

        Assert.Equal("15", settings.Settings.Single(s => s.Key == CostingSettingsKeys.EstimatedMarginPercent).Value);
    }
}
