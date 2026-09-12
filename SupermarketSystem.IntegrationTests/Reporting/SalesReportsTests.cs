using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Application.Reporting.GetBestCashiers;
using SupermarketSystem.Application.Reporting.GetBestCustomers;
using SupermarketSystem.Application.Reporting.GetSalesSummary;
using SupermarketSystem.Application.Reporting.GetVoidedSales;
using SupermarketSystem.Domain.Sales;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Reporting;

[Collection(DatabaseCollection.Name)]
public sealed class GetSalesSummaryTests : IntegrationTestBase
{
    public GetSalesSummaryTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task يحسب_الملخص_ويستثني_الفواتير_الملغاة()
    {
        using var scope = CreateScope();
        // بيانات التقارير كلها كيانات Branch-owned - راجع تعليق TestAuthContext.
        scope.ActAsCrossBranchUser();
        var db = CreateDbContext(scope);
        var categoryId = await ReportingTestDataBuilder.CreateCategoryAsync(db, "تصنيف");
        var (productId, unitId) = await ReportingTestDataBuilder.CreateProductAsync(db, categoryId, "منتج");

        await ReportingTestDataBuilder.CreateCompletedSaleAsync(db, Fixture.TestBranchId, productId, unitId, 2, 10m);
        var voided = await ReportingTestDataBuilder.CreateCompletedSaleAsync(db, Fixture.TestBranchId, productId, unitId, 5, 100m);
        await ReportingTestDataBuilder.VoidSaleAsync(db, voided, Fixture.AdminUserId);

        var handler = scope.ServiceProvider.GetRequiredService<GetSalesSummaryHandler>();
        var result = await handler.HandleAsync(
            new GetSalesSummaryQuery(Fixture.TestBranchId, DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddMinutes(5), null, null),
            CancellationToken.None);

        Assert.Equal(1, result.Period.InvoiceCount);
        Assert.Equal(20m, result.Period.TotalSales);
        Assert.Equal(20m, result.Period.NetRevenue);
        Assert.Null(result.ComparisonPeriod);
        Assert.Null(result.NetRevenueChangePercent);
    }

    [Fact]
    public async Task مقارنة_فترتين_تحسب_نسبة_التغيير_الصحيحة()
    {
        using var scope = CreateScope();
        // بيانات التقارير كلها كيانات Branch-owned - راجع تعليق TestAuthContext.
        scope.ActAsCrossBranchUser();
        var db = CreateDbContext(scope);
        var categoryId = await ReportingTestDataBuilder.CreateCategoryAsync(db, "تصنيف");
        var (productId, unitId) = await ReportingTestDataBuilder.CreateProductAsync(db, categoryId, "منتج");

        var now = DateTime.UtcNow;
        await ReportingTestDataBuilder.CreateCompletedSaleAsync(
            db, Fixture.TestBranchId, productId, unitId, 1, 100m, createdAtUtc: now.AddDays(-1));
        await ReportingTestDataBuilder.CreateCompletedSaleAsync(
            db, Fixture.TestBranchId, productId, unitId, 1, 150m, createdAtUtc: now.AddDays(-10));

        var handler = scope.ServiceProvider.GetRequiredService<GetSalesSummaryHandler>();
        var result = await handler.HandleAsync(
            new GetSalesSummaryQuery(
                Fixture.TestBranchId,
                now.AddDays(-2), now.AddHours(1),
                now.AddDays(-11), now.AddDays(-9)),
            CancellationToken.None);

        Assert.Equal(100m, result.Period.NetRevenue);
        Assert.NotNull(result.ComparisonPeriod);
        Assert.Equal(150m, result.ComparisonPeriod!.NetRevenue);
        // (100 - 150) / 150 * 100 = -33.33..%
        Assert.NotNull(result.NetRevenueChangePercent);
        Assert.True(result.NetRevenueChangePercent < 0);
    }
}

[Collection(DatabaseCollection.Name)]
public sealed class GetBestCashiersTests : IntegrationTestBase
{
    public GetBestCashiersTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task فاتورة_بلا_مستخدم_مسجَّل_تظهر_كـغير_معروف_بدل_ما_تختفي()
    {
        using var scope = CreateScope();
        // بيانات التقارير كلها كيانات Branch-owned - راجع تعليق TestAuthContext.
        scope.ActAsCrossBranchUser();
        var db = CreateDbContext(scope);
        var categoryId = await ReportingTestDataBuilder.CreateCategoryAsync(db, "تصنيف");
        var (productId, unitId) = await ReportingTestDataBuilder.CreateProductAsync(db, categoryId, "منتج");

        await ReportingTestDataBuilder.CreateCompletedSaleAsync(db, Fixture.TestBranchId, productId, unitId, 3, 10m);

        var handler = scope.ServiceProvider.GetRequiredService<GetBestCashiersHandler>();
        var result = await handler.HandleAsync(
            new GetBestCashiersQuery(new PagedRequest(), Fixture.TestBranchId, DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddMinutes(5)),
            CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Null(item.CashierUserId);
        Assert.Equal("(غير معروف)", item.CashierUsername);
        Assert.Equal(30m, item.TotalSales);
        Assert.Equal(1, item.InvoiceCount);
    }
}

[Collection(DatabaseCollection.Name)]
public sealed class GetBestCustomersTests : IntegrationTestBase
{
    public GetBestCustomersTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task يرتب_الزبائن_حسب_إجمالي_المشتريات_تنازليًا_ويستثني_زبون_عابر()
    {
        using var scope = CreateScope();
        // بيانات التقارير كلها كيانات Branch-owned - راجع تعليق TestAuthContext.
        scope.ActAsCrossBranchUser();
        var db = CreateDbContext(scope);
        var categoryId = await ReportingTestDataBuilder.CreateCategoryAsync(db, "تصنيف");
        var (productId, unitId) = await ReportingTestDataBuilder.CreateProductAsync(db, categoryId, "منتج");

        var bigCustomer = await ReportingTestDataBuilder.CreateCustomerAsync(db, "زبون كبير", "0790000001");
        var smallCustomer = await ReportingTestDataBuilder.CreateCustomerAsync(db, "زبون صغير", "0790000002");

        await ReportingTestDataBuilder.CreateCompletedSaleAsync(db, Fixture.TestBranchId, productId, unitId, 1, 200m, bigCustomer);
        await ReportingTestDataBuilder.CreateCompletedSaleAsync(db, Fixture.TestBranchId, productId, unitId, 1, 20m, smallCustomer);
        // زبون عابر (بلا CustomerId) - ما لازم يظهر بهاد التقرير إطلاقًا.
        await ReportingTestDataBuilder.CreateCompletedSaleAsync(db, Fixture.TestBranchId, productId, unitId, 1, 5000m, customerId: null);

        var handler = scope.ServiceProvider.GetRequiredService<GetBestCustomersHandler>();
        var result = await handler.HandleAsync(
            new GetBestCustomersQuery(new PagedRequest(), Fixture.TestBranchId, DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddMinutes(5)),
            CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal("زبون كبير", result.Items[0].FullName);
        Assert.Equal(200m, result.Items[0].TotalPurchases);
        Assert.Equal("زبون صغير", result.Items[1].FullName);
    }
}

[Collection(DatabaseCollection.Name)]
public sealed class GetVoidedSalesTests : IntegrationTestBase
{
    public GetVoidedSalesTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task يرجع_الفاتورة_الملغاة_مع_اسم_من_ألغاها_وسبب_الإلغاء_الصحيح()
    {
        using var scope = CreateScope();
        // بيانات التقارير كلها كيانات Branch-owned - راجع تعليق TestAuthContext.
        scope.ActAsCrossBranchUser();
        var db = CreateDbContext(scope);
        var categoryId = await ReportingTestDataBuilder.CreateCategoryAsync(db, "تصنيف");
        var (productId, unitId) = await ReportingTestDataBuilder.CreateProductAsync(db, categoryId, "منتج");

        var invoice = await ReportingTestDataBuilder.CreateCompletedSaleAsync(db, Fixture.TestBranchId, productId, unitId, 1, 50m);
        await ReportingTestDataBuilder.VoidSaleAsync(db, invoice, Fixture.AdminUserId, VoidReason.CustomerCancelled);

        var handler = scope.ServiceProvider.GetRequiredService<GetVoidedSalesHandler>();
        var result = await handler.HandleAsync(
            new GetVoidedSalesQuery(new PagedRequest(), Fixture.TestBranchId, null, null), CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(invoice.Id, item.SaleInvoiceId);
        Assert.Equal(Fixture.AdminUserId, item.VoidedByUserId);
        Assert.Equal(DatabaseFixture.AdminUsername, item.VoidedByUsername);
        // تحقق فعلي من قيمة الـenum المرجعة - لا فقط عدم انفجار الاستعلام
        // (راجع تحذير CLAUDE.md §3.1 من ترجمة enum.ToString() الخاطئة بـSQL).
        Assert.Equal(VoidReason.CustomerCancelled, item.VoidReason);
    }
}
