using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Application.Reporting.GetManualDiscounts;
using SupermarketSystem.Domain.Sales;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Reporting;

/// <summary>
/// خصم يدوي = خصم بلا DiscountId (Architecture Review §16.10/§13.6: الغياب
/// نفسه هو العلامة، لا حقل منفصل). التقرير يجمع مستوى السطر ومستوى
/// الفاتورة معًا (UNION ALL) - لازم اختبار الحالتين معًا بنفس التقرير.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class GetManualDiscountsTests : IntegrationTestBase
{
    public GetManualDiscountsTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task يرجع_خصم_سطر_وخصم_فاتورة_يدويّين_معًا_ويستثني_فاتورة_بلا_خصم()
    {
        using var scope = CreateScope();
        // بيانات التقارير كلها كيانات Branch-owned - راجع تعليق TestAuthContext.
        scope.ActAsCrossBranchUser();
        var db = CreateDbContext(scope);
        var categoryId = await ReportingTestDataBuilder.CreateCategoryAsync(db, "تصنيف");
        var (productId, unitId) = await ReportingTestDataBuilder.CreateProductAsync(db, categoryId, "منتج");

        // فاتورة بخصم يدوي على مستوى السطر.
        var lineDiscountInvoice = new SaleInvoice(Fixture.TestBranchId, $"INV-{Guid.NewGuid():N}"[..15], Guid.NewGuid(), null, null, null);
        lineDiscountInvoice.AddItem(productId, unitId, 2, 10m, discountSnapshot: 3m, discountId: null);
        db.SaleInvoices.Add(lineDiscountInvoice);

        // فاتورة بخصم يدوي على مستوى الفاتورة كاملة.
        var invoiceDiscountInvoice = new SaleInvoice(Fixture.TestBranchId, $"INV-{Guid.NewGuid():N}"[..15], Guid.NewGuid(), null, null, null);
        invoiceDiscountInvoice.AddItem(productId, unitId, 1, 20m, 0m, null);
        invoiceDiscountInvoice.ApplyInvoiceLevelDiscount(discountId: null, discountAmountSnapshot: 5m);
        db.SaleInvoices.Add(invoiceDiscountInvoice);

        // فاتورة بلا أي خصم - ما لازم تظهر إطلاقًا.
        var plainInvoice = new SaleInvoice(Fixture.TestBranchId, $"INV-{Guid.NewGuid():N}"[..15], Guid.NewGuid(), null, null, null);
        plainInvoice.AddItem(productId, unitId, 1, 15m, 0m, null);
        db.SaleInvoices.Add(plainInvoice);

        await db.SaveChangesAsync(CancellationToken.None);

        var handler = scope.ServiceProvider.GetRequiredService<GetManualDiscountsHandler>();
        var result = await handler.HandleAsync(
            new GetManualDiscountsQuery(new PagedRequest(), Fixture.TestBranchId, null, null), CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.Contains(result.Items, i => i.Level == "Line" && i.DiscountAmount == 3m && i.ProductId == productId);
        Assert.Contains(result.Items, i => i.Level == "Invoice" && i.DiscountAmount == 5m && i.ProductId == null);
    }
}
