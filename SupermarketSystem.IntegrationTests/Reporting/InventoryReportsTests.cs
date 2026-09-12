using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Application.Reporting.GetCurrentCapitalValue;
using SupermarketSystem.Application.Reporting.GetNegativeStock;
using SupermarketSystem.Application.Reporting.GetProductConsumptionLevels;
using SupermarketSystem.Application.Reporting.GetReorderNeededProducts;
using SupermarketSystem.Application.Reporting.GetStagnantProducts;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Reporting;

[Collection(DatabaseCollection.Name)]
public sealed class GetNegativeStockTests : IntegrationTestBase
{
    public GetNegativeStockTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task يرجع_فقط_الأصناف_برصيد_سالب()
    {
        using var scope = CreateScope();
        // بيانات التقارير كلها كيانات Branch-owned - راجع تعليق TestAuthContext.
        scope.ActAsCrossBranchUser();
        var db = CreateDbContext(scope);
        var categoryId = await ReportingTestDataBuilder.CreateCategoryAsync(db, "تصنيف");
        var (negativeProductId, _) = await ReportingTestDataBuilder.CreateProductAsync(db, categoryId, "منتج سالب");
        var (positiveProductId, _) = await ReportingTestDataBuilder.CreateProductAsync(db, categoryId, "منتج موجب");

        await ReportingTestDataBuilder.SetStockAsync(db, negativeProductId, Fixture.TestBranchId, -3m);
        await ReportingTestDataBuilder.SetStockAsync(db, positiveProductId, Fixture.TestBranchId, 5m);

        var handler = scope.ServiceProvider.GetRequiredService<GetNegativeStockHandler>();
        var result = await handler.HandleAsync(
            new GetNegativeStockQuery(new PagedRequest(), Fixture.TestBranchId), CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(negativeProductId, item.ProductId);
        Assert.Equal(-3m, item.QuantityOnHand);
    }
}

[Collection(DatabaseCollection.Name)]
public sealed class GetReorderNeededProductsTests : IntegrationTestBase
{
    public GetReorderNeededProductsTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task منتج_تحت_الحد_الأدنى_يظهر_ومنتج_فوقه_لا_يظهر()
    {
        using var scope = CreateScope();
        // بيانات التقارير كلها كيانات Branch-owned - راجع تعليق TestAuthContext.
        scope.ActAsCrossBranchUser();
        var db = CreateDbContext(scope);
        var categoryId = await ReportingTestDataBuilder.CreateCategoryAsync(db, "تصنيف");

        var (lowProductId, _) = await ReportingTestDataBuilder.CreateProductAsync(db, categoryId, "منتج محتاج طلب");
        await ReportingTestDataBuilder.CreateProductBranchAsync(db, lowProductId, Fixture.TestBranchId, 5m, minimumStock: 10m);
        await ReportingTestDataBuilder.SetStockAsync(db, lowProductId, Fixture.TestBranchId, 4m);

        var (healthyProductId, _) = await ReportingTestDataBuilder.CreateProductAsync(db, categoryId, "منتج مخزونه كافٍ");
        await ReportingTestDataBuilder.CreateProductBranchAsync(db, healthyProductId, Fixture.TestBranchId, 5m, minimumStock: 10m);
        await ReportingTestDataBuilder.SetStockAsync(db, healthyProductId, Fixture.TestBranchId, 50m);

        var handler = scope.ServiceProvider.GetRequiredService<GetReorderNeededProductsHandler>();
        var result = await handler.HandleAsync(
            new GetReorderNeededProductsQuery(new PagedRequest(), Fixture.TestBranchId), CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(lowProductId, item.ProductId);
        Assert.Equal(4m, item.CurrentStock);
        Assert.Equal(10m, item.MinimumStock);
    }
}

[Collection(DatabaseCollection.Name)]
public sealed class GetCurrentCapitalValueTests : IntegrationTestBase
{
    public GetCurrentCapitalValueTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task يحسب_متوسط_التكلفة_المرجّح_من_فواتير_شراء_مستلمة_فقط()
    {
        using var scope = CreateScope();
        // بيانات التقارير كلها كيانات Branch-owned - راجع تعليق TestAuthContext.
        scope.ActAsCrossBranchUser();
        var db = CreateDbContext(scope);
        var categoryId = await ReportingTestDataBuilder.CreateCategoryAsync(db, "تصنيف");
        var (productId, unitId) = await ReportingTestDataBuilder.CreateProductAsync(db, categoryId, "منتج");
        var supplierId = await ReportingTestDataBuilder.CreateSupplierAsync(db, "مورّد");

        // دفعة 10 وحدات بـ4، ودفعة 10 وحدات بـ6 => متوسط مرجّح = 5.
        await ReportingTestDataBuilder.CreateReceivedPurchaseAsync(db, Fixture.TestBranchId, supplierId, productId, unitId, 10m, 4m);
        await ReportingTestDataBuilder.CreateReceivedPurchaseAsync(db, Fixture.TestBranchId, supplierId, productId, unitId, 10m, 6m);
        await ReportingTestDataBuilder.SetStockAsync(db, productId, Fixture.TestBranchId, 15m);

        var handler = scope.ServiceProvider.GetRequiredService<GetCurrentCapitalValueHandler>();
        var result = await handler.HandleAsync(
            new GetCurrentCapitalValueQuery(new PagedRequest(), Fixture.TestBranchId), CancellationToken.None);

        var item = Assert.Single(result.Items.Items);
        Assert.Equal(productId, item.ProductId);
        Assert.Equal(5m, item.WeightedAverageCost);
        Assert.Equal(15m, item.QuantityOnHand);
        Assert.Equal(75m, item.TotalValue);
        Assert.Equal(75m, result.TotalCapitalValue);
        Assert.Equal(0, result.ProductsExcludedNoCostHistory);
    }

    [Fact]
    public async Task منتج_بمخزون_موجب_بلا_تاريخ_شراء_يُستبعد_ويُحسَب_بالعدّاد()
    {
        using var scope = CreateScope();
        // بيانات التقارير كلها كيانات Branch-owned - راجع تعليق TestAuthContext.
        scope.ActAsCrossBranchUser();
        var db = CreateDbContext(scope);
        var categoryId = await ReportingTestDataBuilder.CreateCategoryAsync(db, "تصنيف");
        var (productId, _) = await ReportingTestDataBuilder.CreateProductAsync(db, categoryId, "منتج بلا تاريخ شراء");
        await ReportingTestDataBuilder.SetStockAsync(db, productId, Fixture.TestBranchId, 8m);

        var handler = scope.ServiceProvider.GetRequiredService<GetCurrentCapitalValueHandler>();
        var result = await handler.HandleAsync(
            new GetCurrentCapitalValueQuery(new PagedRequest(), Fixture.TestBranchId), CancellationToken.None);

        Assert.Empty(result.Items.Items);
        Assert.Equal(0m, result.TotalCapitalValue);
        Assert.Equal(1, result.ProductsExcludedNoCostHistory);
    }
}

[Collection(DatabaseCollection.Name)]
public sealed class GetStagnantProductsTests : IntegrationTestBase
{
    public GetStagnantProductsTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task منتج_متاح_بلا_مبيعات_منذ_التاريخ_المحدد_يظهر_راكدًا()
    {
        using var scope = CreateScope();
        // بيانات التقارير كلها كيانات Branch-owned - راجع تعليق TestAuthContext.
        scope.ActAsCrossBranchUser();
        var db = CreateDbContext(scope);
        var categoryId = await ReportingTestDataBuilder.CreateCategoryAsync(db, "تصنيف");

        var (stagnantId, _) = await ReportingTestDataBuilder.CreateProductAsync(db, categoryId, "منتج راكد");
        await ReportingTestDataBuilder.CreateProductBranchAsync(db, stagnantId, Fixture.TestBranchId, 7m);

        var (activeId, activeUnitId) = await ReportingTestDataBuilder.CreateProductAsync(db, categoryId, "منتج نشط");
        await ReportingTestDataBuilder.CreateProductBranchAsync(db, activeId, Fixture.TestBranchId, 9m);
        await ReportingTestDataBuilder.CreateCompletedSaleAsync(db, Fixture.TestBranchId, activeId, activeUnitId, 1, 9m);

        var handler = scope.ServiceProvider.GetRequiredService<GetStagnantProductsHandler>();
        var result = await handler.HandleAsync(
            new GetStagnantProductsQuery(new PagedRequest(), Fixture.TestBranchId, DateTime.UtcNow.AddDays(-30)),
            CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(stagnantId, item.ProductId);
        Assert.Equal(7m, item.SellingPrice);
        // ما اتشرى قط - رصيد Stock null لا صفر مضلَّل.
        Assert.Null(item.CurrentStock);
    }
}

[Collection(DatabaseCollection.Name)]
public sealed class GetProductConsumptionLevelsTests : IntegrationTestBase
{
    public GetProductConsumptionLevelsTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task يصنّف_المنتج_عالي_الاستهلاك_بعنوان_عربي_صحيح()
    {
        using var scope = CreateScope();
        // بيانات التقارير كلها كيانات Branch-owned - راجع تعليق TestAuthContext.
        scope.ActAsCrossBranchUser();
        var db = CreateDbContext(scope);
        var categoryId = await ReportingTestDataBuilder.CreateCategoryAsync(db, "تصنيف");
        var (productId, unitId) = await ReportingTestDataBuilder.CreateProductAsync(db, categoryId, "منتج عالي الاستهلاك");
        await ReportingTestDataBuilder.CreateProductBranchAsync(db, productId, Fixture.TestBranchId, 3m);

        // الحد الافتراضي لـ"عالي" هو 50 فأكثر (ConsumptionLevelSettingsKeys.HighThreshold).
        await ReportingTestDataBuilder.CreateCompletedSaleAsync(db, Fixture.TestBranchId, productId, unitId, 60m, 3m);

        var handler = scope.ServiceProvider.GetRequiredService<GetProductConsumptionLevelsHandler>();
        var result = await handler.HandleAsync(
            new GetProductConsumptionLevelsQuery(new PagedRequest(), Fixture.TestBranchId, DateTime.UtcNow.AddDays(-1)),
            CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(60m, item.QuantitySold);
        Assert.Equal((int)ConsumptionLevel.High, item.LevelCode);
        Assert.Equal("عالي", item.LevelTitle);
    }

    [Fact]
    public async Task منتج_متاح_بلا_أي_مبيعات_يصنَّف_شبه_معدوم()
    {
        using var scope = CreateScope();
        // بيانات التقارير كلها كيانات Branch-owned - راجع تعليق TestAuthContext.
        scope.ActAsCrossBranchUser();
        var db = CreateDbContext(scope);
        var categoryId = await ReportingTestDataBuilder.CreateCategoryAsync(db, "تصنيف");
        var (productId, _) = await ReportingTestDataBuilder.CreateProductAsync(db, categoryId, "منتج بلا حركة");
        await ReportingTestDataBuilder.CreateProductBranchAsync(db, productId, Fixture.TestBranchId, 3m);

        var handler = scope.ServiceProvider.GetRequiredService<GetProductConsumptionLevelsHandler>();
        var result = await handler.HandleAsync(
            new GetProductConsumptionLevelsQuery(new PagedRequest(), Fixture.TestBranchId, DateTime.UtcNow.AddDays(-1)),
            CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(0m, item.QuantitySold);
        Assert.Equal((int)ConsumptionLevel.NearZero, item.LevelCode);
        Assert.Equal("شبه معدوم", item.LevelTitle);
    }
}
