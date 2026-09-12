using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Application.Reporting.GetSupplierPriceComparison;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Reporting;

[Collection(DatabaseCollection.Name)]
public sealed class GetSupplierPriceComparisonTests : IntegrationTestBase
{
    public GetSupplierPriceComparisonTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task يقارن_أسعار_موردين_مختلفين_لنفس_المنتج()
    {
        using var scope = CreateScope();
        // بيانات التقارير كلها كيانات Branch-owned - راجع تعليق TestAuthContext.
        scope.ActAsCrossBranchUser();
        var db = CreateDbContext(scope);
        var categoryId = await ReportingTestDataBuilder.CreateCategoryAsync(db, "تصنيف");
        var (productId, unitId) = await ReportingTestDataBuilder.CreateProductAsync(db, categoryId, "منتج مقارن");
        var supplierA = await ReportingTestDataBuilder.CreateSupplierAsync(db, "مورّد أ");
        var supplierB = await ReportingTestDataBuilder.CreateSupplierAsync(db, "مورّد ب");

        await ReportingTestDataBuilder.CreateReceivedPurchaseAsync(db, Fixture.TestBranchId, supplierA, productId, unitId, 10m, 4m);
        await ReportingTestDataBuilder.CreateReceivedPurchaseAsync(db, Fixture.TestBranchId, supplierB, productId, unitId, 10m, 5m);

        var handler = scope.ServiceProvider.GetRequiredService<GetSupplierPriceComparisonHandler>();
        var result = await handler.HandleAsync(
            new GetSupplierPriceComparisonQuery(new PagedRequest(), productId, null, null), CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.Contains(result.Items, i => i.SupplierName == "مورّد أ" && i.UnitCost == 4m);
        Assert.Contains(result.Items, i => i.SupplierName == "مورّد ب" && i.UnitCost == 5m);
    }

    [Fact]
    public async Task فاتورة_شراء_بحالة_Draft_لا_تظهر_بالمقارنة()
    {
        using var scope = CreateScope();
        // بيانات التقارير كلها كيانات Branch-owned - راجع تعليق TestAuthContext.
        scope.ActAsCrossBranchUser();
        var db = CreateDbContext(scope);
        var categoryId = await ReportingTestDataBuilder.CreateCategoryAsync(db, "تصنيف");
        var (productId, unitId) = await ReportingTestDataBuilder.CreateProductAsync(db, categoryId, "منتج");
        var supplierId = await ReportingTestDataBuilder.CreateSupplierAsync(db, "مورّد");

        var draftInvoice = new SupermarketSystem.Domain.Purchasing.PurchaseInvoice(
            Fixture.TestBranchId, supplierId, $"PUR-{Guid.NewGuid():N}"[..15], null);
        draftInvoice.AddItem(productId, unitId, null, 5m, 3m);
        db.PurchaseInvoices.Add(draftInvoice);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = scope.ServiceProvider.GetRequiredService<GetSupplierPriceComparisonHandler>();
        var result = await handler.HandleAsync(
            new GetSupplierPriceComparisonQuery(new PagedRequest(), productId, null, null), CancellationToken.None);

        Assert.Empty(result.Items);
    }
}
