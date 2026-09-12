using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Application.Reporting.GetRecentReturnedItems;
using SupermarketSystem.Application.Reporting.GetRecentReturns;
using SupermarketSystem.Application.Reporting.GetReturnFrequencyByProduct;
using SupermarketSystem.Domain.Sales;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Reporting;

[Collection(DatabaseCollection.Name)]
public sealed class GetRecentReturnsTests : IntegrationTestBase
{
    public GetRecentReturnsTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task يرجع_الإرجاع_مع_كاشير_غير_محلول_بدل_ما_يختفي()
    {
        using var scope = CreateScope();
        // بيانات التقارير كلها كيانات Branch-owned - راجع تعليق TestAuthContext.
        scope.ActAsCrossBranchUser();
        var db = CreateDbContext(scope);
        var categoryId = await ReportingTestDataBuilder.CreateCategoryAsync(db, "تصنيف");
        var (productId, unitId) = await ReportingTestDataBuilder.CreateProductAsync(db, categoryId, "منتج");

        var sale = await ReportingTestDataBuilder.CreateCompletedSaleAsync(db, Fixture.TestBranchId, productId, unitId, 3, 10m);
        var returnInvoice = await ReportingTestDataBuilder.CreateReturnAsync(
            db, sale, productId, unitId, 1, 10m, ReturnReason.Defective);

        var handler = scope.ServiceProvider.GetRequiredService<GetRecentReturnsHandler>();
        var result = await handler.HandleAsync(
            new GetRecentReturnsQuery(new PagedRequest(), Fixture.TestBranchId, null, null), CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(returnInvoice.Id, item.ReturnInvoiceId);
        Assert.Equal(sale.Id, item.OriginalSaleInvoiceId);
        Assert.Equal(ReturnReason.Defective, item.Reason);
        Assert.Null(item.CashierUserId);
        Assert.Equal("(unresolved)", item.CashierUsername);
    }
}

[Collection(DatabaseCollection.Name)]
public sealed class GetRecentReturnedItemsTests : IntegrationTestBase
{
    public GetRecentReturnedItemsTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task يرجع_سطر_الإرجاع_بغض_النظر_عن_الفاتورة()
    {
        using var scope = CreateScope();
        // بيانات التقارير كلها كيانات Branch-owned - راجع تعليق TestAuthContext.
        scope.ActAsCrossBranchUser();
        var db = CreateDbContext(scope);
        var categoryId = await ReportingTestDataBuilder.CreateCategoryAsync(db, "تصنيف");
        var (productId, unitId) = await ReportingTestDataBuilder.CreateProductAsync(db, categoryId, "منتج مرتجع");

        var sale = await ReportingTestDataBuilder.CreateCompletedSaleAsync(db, Fixture.TestBranchId, productId, unitId, 5, 8m);
        await ReportingTestDataBuilder.CreateReturnAsync(db, sale, productId, unitId, 2, 8m, ReturnReason.WrongItem);

        var handler = scope.ServiceProvider.GetRequiredService<GetRecentReturnedItemsHandler>();
        var result = await handler.HandleAsync(
            new GetRecentReturnedItemsQuery(new PagedRequest(), Fixture.TestBranchId, null, null), CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(productId, item.ProductId);
        Assert.Equal("منتج مرتجع", item.ProductName);
        Assert.Equal(2m, item.Quantity);
        Assert.Equal(16m, item.LineTotal);
    }
}

[Collection(DatabaseCollection.Name)]
public sealed class GetReturnFrequencyByProductTests : IntegrationTestBase
{
    public GetReturnFrequencyByProductTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task يجمع_عدد_وقيمة_مرتجعات_نفس_المنتج_عبر_أكثر_من_فاتورة()
    {
        using var scope = CreateScope();
        // بيانات التقارير كلها كيانات Branch-owned - راجع تعليق TestAuthContext.
        scope.ActAsCrossBranchUser();
        var db = CreateDbContext(scope);
        var categoryId = await ReportingTestDataBuilder.CreateCategoryAsync(db, "تصنيف");
        var (productId, unitId) = await ReportingTestDataBuilder.CreateProductAsync(db, categoryId, "منتج كثير الإرجاع");

        var firstSale = await ReportingTestDataBuilder.CreateCompletedSaleAsync(db, Fixture.TestBranchId, productId, unitId, 5, 10m);
        var secondSale = await ReportingTestDataBuilder.CreateCompletedSaleAsync(db, Fixture.TestBranchId, productId, unitId, 5, 10m);

        await ReportingTestDataBuilder.CreateReturnAsync(db, firstSale, productId, unitId, 1, 10m, ReturnReason.Defective);
        await ReportingTestDataBuilder.CreateReturnAsync(db, secondSale, productId, unitId, 2, 10m, ReturnReason.Expired);

        var handler = scope.ServiceProvider.GetRequiredService<GetReturnFrequencyByProductHandler>();
        var result = await handler.HandleAsync(
            new GetReturnFrequencyByProductQuery(
                new PagedRequest(), Fixture.TestBranchId, DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddMinutes(5)),
            CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(productId, item.ProductId);
        Assert.Equal(2, item.ReturnCount);
        Assert.Equal(3m, item.TotalQuantityReturned);
        Assert.Equal(30m, item.TotalValueReturned);
    }
}
