using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Catalog.CreatePromotion;
using SupermarketSystem.Application.Sales.CompleteSale;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Sales;

/// <summary>
/// عروض الكمية على الكرتونة (9/10/2026، قرار صاحب المشروع): العرض ("3 بدينار" والحبة 0.500) بيتحسب بالوحدة الأساسية حتى لو البيع
/// بكرتونة (12 حبة) - كمية السطر × معامل التحويل، بسعر الحبة العادي. للحبة المعامل 1 فالسلوك القديم نفسه.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class CartonPromotionTests : IntegrationTestBase
{
    public CartonPromotionTests(DatabaseFixture fixture) : base(fixture) { }

    private sealed record Setup(Guid ProductId, Guid CartonUnitId, Guid PromotionId);

    /// <summary>علبة فول: الحبة 0.500، الكرتونة 12 حبة (6.000)، عرض "3 علب بدينار".</summary>
    private async Task<Setup> SetUpAsync(IServiceScope scope, string name, decimal? maxBaseQuantity = null)
    {
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        await TestDataBuilder.EnsureDocumentSequencesProvisionedAsync(db, Fixture.TestBranchId);
        // الوحدتين قبل أول حفظ (إضافة وحدة لمنتج محفوظ بيحسبها EF تعديل مش إضافة لأن مفتاحها Guid جاهز).
        var category = await TestDataBuilder.CreateCategoryAsync(db);
        var product = new SupermarketSystem.Domain.Catalog.Product(name, category.Id, false);
        product.ChangeStatus(SupermarketSystem.Domain.Catalog.ProductStatus.Active);
        product.AddUnit("حبة", 1m, isBaseUnit: true);
        var carton = product.AddUnit("كرتونة", 12m, isBaseUnit: false);
        db.Products.Add(product);
        await db.SaveChangesAsync();
        await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, sellingPrice: 0.500m);
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 1000m);

        var promo = await scope.ServiceProvider.GetRequiredService<CreatePromotionHandler>().HandleAsync(new CreatePromotionCommand(
            product.Id, "3 علب بدينار", 3, 1.000m, maxBaseQuantity, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(10),
            new[] { Fixture.TestBranchId }), CancellationToken.None);
        Assert.True(promo.IsSuccess, promo.Error?.Message);
        return new Setup(product.Id, carton.Id, promo.Value.PromotionId);
    }

    private async Task<(CompleteSaleResponse Response, decimal PromotionAmount, Guid? PromotionId)> SellCartonsAsync(
        IServiceScope scope, Setup s, decimal cartons, decimal paid, long? catalogVersion = null, Guid? claimedPromotionId = null)
    {
        var result = await scope.ServiceProvider.GetRequiredService<CompleteSaleHandler>().HandleAsync(new CompleteSaleCommand(
            Fixture.TestBranchId, Guid.NewGuid(), null, 0m,
            new[] { new CompleteSaleItemDto(s.ProductId, s.CartonUnitId, cartons, 0m, null, catalogVersion, claimedPromotionId) },
            new[] { new CompleteSalePaymentDto(TestDataBuilder.CashPaymentMethodId, paid, null, Guid.NewGuid()) }),
            CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error?.Message);
        var db = CreateDbContext(scope);
        var line = await db.SaleInvoiceItems.AsNoTracking().SingleAsync(i => i.SaleInvoiceId == result.Value.SaleInvoiceId);
        return (result.Value, line.PromotionAmount, line.PromotionId);
    }

    [Fact]
    public async Task كرتونة_12_علبة_بعرض_3_بدينار_بتنباع_بأربع_حزم_بدل_السعر_الكامل()
    {
        using var scope = CreateScope();
        var s = await SetUpAsync(scope, "علب فول كرتونة");

        // 12 علبة = 4 حزم × 1.000 = 4.000 (بدل 6.000) - التوفير 2.000
        var sale = await SellCartonsAsync(scope, s, cartons: 1m, paid: 4.000m);
        Assert.Equal(4.000m, sale.Response.TotalAmount);
        Assert.Equal(2.000m, sale.PromotionAmount);
        Assert.Equal(s.PromotionId, sale.PromotionId);
    }

    [Fact]
    public async Task سطر_الكاشير_بيحترم_قراره_بالعرض_على_الكرتونة_وبلاه_بالسعر_الكامل()
    {
        using var scope = CreateScope();
        var s = await SetUpAsync(scope, "علب فول كاشير");

        var claimed = await SellCartonsAsync(scope, s, 2m, paid: 8.000m, catalogVersion: 1, claimedPromotionId: s.PromotionId);
        Assert.Equal(8.000m, claimed.Response.TotalAmount);
        Assert.Equal(4.000m, claimed.PromotionAmount);

        var notClaimed = await SellCartonsAsync(scope, s, 1m, paid: 6.000m, catalogVersion: 1, claimedPromotionId: null);
        Assert.Equal(6.000m, notClaimed.Response.TotalAmount);
        Assert.Equal(0m, notClaimed.PromotionAmount);
        Assert.Null(notClaimed.PromotionId);
    }

    [Fact]
    public async Task سقف_العرض_بالفاتورة_بالوحدة_الأساسية_والباقي_بالسعر_العادي()
    {
        using var scope = CreateScope();
        var s = await SetUpAsync(scope, "علب فول بسقف", maxBaseQuantity: 6m);

        // 12 علبة، السقف 6 علب فقط للعرض = حزمتين (2.000) + 6 علب عادية (3.000) = 5.000
        var sale = await SellCartonsAsync(scope, s, 1m, paid: 5.000m);
        Assert.Equal(5.000m, sale.Response.TotalAmount);
        Assert.Equal(1.000m, sale.PromotionAmount);
    }
}
