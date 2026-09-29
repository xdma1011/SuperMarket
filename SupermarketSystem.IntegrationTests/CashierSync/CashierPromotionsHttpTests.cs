using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Catalog.CreatePromotion;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.CashierSync;

/// <summary>
/// عروض الكمية من الكاشير (29/9/2026) - كان الكاشير يتجاهل العروض والسيرفر يطبّقها، فالدفعة أكبر من الفاتورة
/// (422) وكل بيعة لمنتج عليه عرض تعلق بالطابور. هلق السيرفر بيحترم قرار الكاشير لكل سطر (OFFLINE PROMOTION):
/// عرض طبّقه الكاشير = بيتطبّق بتعريفه على السيرفر؛ بلا عرض = سعر كامل. نفس شكل طلب تطبيق الويندوز.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class CashierPromotionsHttpTests : IntegrationTestBase
{
    public CashierPromotionsHttpTests(DatabaseFixture fixture) : base(fixture) { }

    private sealed record Setup(HttpClient Cashier, Guid ProductId, Guid UnitId, Guid PromotionId, long CatalogVersion);

    /// <summary>منتج بدينار، عرض "3 بـ2.500" شغّال، وعرض مجدول يبلّش بعد يومين (للمزامنة).</summary>
    private async Task<Setup> SetUpAsync(string name)
    {
        Guid productId, unitId, promotionId;
        using (var scope = CreateScope())
        {
            await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
            var db = CreateDbContext(scope);
            await TestDataBuilder.EnsureDocumentSequencesProvisionedAsync(db, Fixture.TestBranchId);
            var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, name);
            await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, sellingPrice: 1.000m);
            await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 100m);
            var create = scope.ServiceProvider.GetRequiredService<CreatePromotionHandler>();
            var promo = await create.HandleAsync(new CreatePromotionCommand(
                product.Id, "3 بـ2.500", 3, 2.500m, null, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(10), new[] { Fixture.TestBranchId }),
                CancellationToken.None);
            Assert.True(promo.IsSuccess);
            await create.HandleAsync(new CreatePromotionCommand(
                product.Id, "عرض الأسبوع الجاي", 2, 1.500m, null, DateTime.UtcNow.AddDays(2), DateTime.UtcNow.AddDays(9), new[] { Fixture.TestBranchId }),
                CancellationToken.None);
            productId = product.Id;
            unitId = unit.Id;
            promotionId = promo.Value.PromotionId;
        }

        var (_, cashierName) = await UsersTestDataHelper.CreateUserWithRoleReturningUsernameAsync(
            Fixture.Factory.Services, Fixture.TestBranchId, UsersTestDataHelper.CashierRoleId, "promo.cashier");
        var cashier = await LoginHelper.LoginAsAsync(Fixture, cashierName, UsersTestDataHelper.DefaultPassword, appType: "Cashier");
        var version = (await cashier.GetFromJsonAsync<JsonElement>("/api/v1/cashier-sync/catalog-version")).GetProperty("version").GetInt64();
        return new Setup(cashier, productId, unitId, promotionId, version);
    }

    private Task<HttpResponseMessage> SellAsync(Setup s, decimal quantity, decimal paid, Guid? promotionId, Guid? productId = null) =>
        s.Cashier.PostAsJsonAsync("/api/v1/sales", new
        {
            branchId = Fixture.TestBranchId, clientRequestId = Guid.NewGuid(), customerId = (Guid?)null, invoiceLevelDiscountAmount = 0m,
            items = new[]
            {
                new { productId = productId ?? s.ProductId, productUnitId = s.UnitId, quantity, manualDiscountAmount = 0m,
                      productBatchId = (Guid?)null, catalogVersion = (long?)s.CatalogVersion, promotionId }
            },
            payments = new[] { new { paymentMethodId = TestDataBuilder.CashPaymentMethodId, amount = paid, externalReference = (string?)null, clientRequestId = Guid.NewGuid() } }
        });

    private static async Task<JsonElement> OkJsonAsync(HttpResponseMessage response, string what)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{what}: {(int)response.StatusCode} {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    [Fact]
    public async Task المزامنة_بتبعت_العروض_الشغّالة_والمجدولة_بتواريخها()
    {
        var s = await SetUpAsync("منتج عروض مزامنة");

        var page = await OkJsonAsync(await s.Cashier.GetAsync(
            $"/api/v1/cashier-sync/catalog-page?branchId={Fixture.TestBranchId}&pageNumber=1&pageSize=500"), "مزامنة");
        var item = page.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("productId").GetGuid() == s.ProductId);

        Assert.Equal(s.PromotionId, item.GetProperty("activePromotion").GetProperty("promotionId").GetGuid());
        var promotions = item.GetProperty("promotions").EnumerateArray().ToList();
        Assert.Equal(2, promotions.Count);
        Assert.Equal("3 بـ2.500", promotions[0].GetProperty("title").GetString()); // الأقدم إنشاءً أول (نفس أولوية السيرفر)
        Assert.True(promotions[1].GetProperty("startAtUtc").GetDateTime() > DateTime.UtcNow.AddDays(1));
    }

    [Fact]
    public async Task الكاشير_طبّق_العرض_بينقبل_بسعر_العرض_وبلا_عرض_بينقبل_بالسعر_الكامل()
    {
        var s = await SetUpAsync("منتج عروض بيع");

        // 4 حبات بعرض: 2.500 (حزمة) + 1.000 (الباقي) = 3.500
        var withPromo = await OkJsonAsync(await SellAsync(s, 4m, 3.500m, s.PromotionId), "بيع بعرض");
        Assert.Equal(3.500m, withPromo.GetProperty("totalAmount").GetDecimal());

        // الخلل القديم: الكاشير ما طبّق العرض ودفع السعر الكامل - كان 422، هلق بينقبل بالسعر الكامل
        var noPromo = await OkJsonAsync(await SellAsync(s, 3m, 3.000m, promotionId: null), "بيع بلا عرض");
        Assert.Equal(3.000m, noPromo.GetProperty("totalAmount").GetDecimal());
        Assert.Empty(noPromo.GetProperty("reviewFlags").EnumerateArray());

        using var scope = CreateScope();
        var db = CreateDbContext(scope);
        var promoLine = await db.SaleInvoiceItems.AsNoTracking().SingleAsync(i => i.SaleInvoiceId == withPromo.GetProperty("saleInvoiceId").GetGuid());
        Assert.Equal(0.500m, promoLine.PromotionAmount);
        Assert.Equal(s.PromotionId, promoLine.PromotionId);
    }

    [Fact]
    public async Task عرض_خلص_قبل_وصول_البيعة_بينقبل_بس_بيتعلّم_للمراجعة_وعرض_منتج_تاني_مرفوض()
    {
        var s = await SetUpAsync("منتج عرض خلص");
        using (var scope = CreateScope())
        {
            var db = CreateDbContext(scope);
            var promo = await db.Promotions.SingleAsync(p => p.Id == s.PromotionId);
            promo.UpdateDetails(promo.Title, promo.BundleQuantity, promo.BundlePrice, promo.MaxQuantityPerInvoice,
                DateTime.UtcNow.AddDays(-5), DateTime.UtcNow.AddHours(-1));
            await db.SaveChangesAsync();
        }

        // الكاشير باع أوفلاين والعرض شغّال عنده، والبيعة وصلت بعد ما خلص
        var late = await OkJsonAsync(await SellAsync(s, 3m, 2.500m, s.PromotionId), "بيعة متأخرة");
        Assert.Equal(2.500m, late.GetProperty("totalAmount").GetDecimal());
        Assert.Contains(late.GetProperty("reviewFlags").EnumerateArray(), f => f.GetString()!.Contains("مش شغّال"));

        // عرض منتج تاني على هالمنتج = مرفوض (مش سعر من العميل)
        var other = await SetUpAsync("منتج تاني للعرض");
        var wrong = await SellAsync(s, 3m, 2.500m, other.PromotionId);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, wrong.StatusCode);
    }
}
