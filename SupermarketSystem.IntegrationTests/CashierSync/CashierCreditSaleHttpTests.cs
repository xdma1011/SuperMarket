using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.CashierSync;

/// <summary>
/// زر "بيع بالدين" بالكاشير (29/9/2026): الكاشير بيتأكد من الزبون برقمه (GET /sales/credit-customer) - اسمه ودينه
/// الحالي - وبعدين بيبعت البيعة بـcustomerId صريح + allowCreditSale، والمستلم (لو في) دفعة جزئية. نفس شكل طلب
/// تطبيق الويندوز (SaleWindow.CompleteSaleAsync) حرفيًا.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class CashierCreditSaleHttpTests : IntegrationTestBase
{
    public CashierCreditSaleHttpTests(DatabaseFixture fixture) : base(fixture) { }

    private async Task<(HttpClient Cashier, Guid ProductId, Guid UnitId, Guid CustomerId)> SetUpAsync()
    {
        Guid productId, unitId, customerId;
        using (var scope = CreateScope())
        {
            await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
            var db = CreateDbContext(scope);
            await TestDataBuilder.EnsureDocumentSequencesProvisionedAsync(db, Fixture.TestBranchId);
            var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج بيع بالدين");
            await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, sellingPrice: 2.250m);
            await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 100m);
            customerId = (await TestDataBuilder.CreateCustomerAsync(db, "أبو خالد", "0791234567")).Id;
            await TestDataBuilder.CreateCustomerAsync(db, "زبون تاني", "0785550000");
            productId = product.Id;
            unitId = unit.Id;
        }

        var (_, cashierName) = await UsersTestDataHelper.CreateUserWithRoleReturningUsernameAsync(
            Fixture.Factory.Services, Fixture.TestBranchId, UsersTestDataHelper.CashierRoleId, "credit.cashier");
        var cashier = await LoginHelper.LoginAsAsync(Fixture, cashierName, UsersTestDataHelper.DefaultPassword, appType: "Cashier");
        return (cashier, productId, unitId, customerId);
    }

    // نفس بناء الطلب بـSaleWindow: الدفعة بتنشال لو مبلغها صفر (بالدين بلا ولا فلس).
    private Task<HttpResponseMessage> SellOnCreditAsync(HttpClient cashier, Guid productId, Guid unitId, Guid? customerId, decimal paidNow, bool allowCreditSale = true) =>
        cashier.PostAsJsonAsync("/api/v1/sales", new
        {
            branchId = Fixture.TestBranchId, clientRequestId = Guid.NewGuid(), customerId, allowCreditSale, invoiceLevelDiscountAmount = 0m,
            items = new[]
            {
                new { productId, productUnitId = unitId, quantity = 2m, manualDiscountAmount = 0m, productBatchId = (Guid?)null,
                      catalogVersion = (long?)null, promotionId = (Guid?)null }
            },
            payments = new[]
            {
                new { paymentMethodId = TestDataBuilder.CashPaymentMethodId, amount = paidNow, externalReference = (string?)null, clientRequestId = Guid.NewGuid() }
            }.Where(p => p.amount > 0).ToArray(),
            preparedOrderId = (Guid?)null,
            customerPhone = "0791234567"
        });

    private static async Task<JsonElement> OkJsonAsync(HttpResponseMessage response, string what)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{what}: {(int)response.StatusCode} {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    [Fact]
    public async Task تأكيد_الزبون_برقمه_ثم_بيع_بالدين_جزئي_وكامل_بيتراكم_على_دينه()
    {
        var (cashier, productId, unitId, customerId) = await SetUpAsync();

        // الرقم بأي صيغة (+962...) بيلاقيه بآخر 9 أرقام، ودينه صفر
        var before = await OkJsonAsync(await cashier.GetAsync("/api/v1/sales/credit-customer?phone=%2B962791234567"), "تأكيد الزبون");
        Assert.Equal(customerId, before.GetProperty("customerId").GetGuid());
        Assert.Equal("أبو خالد", before.GetProperty("fullName").GetString());
        Assert.Equal(0m, before.GetProperty("currentDebt").GetDecimal());

        // 2 × 2.250 = 4.500، دفع 1.000 هلق → 3.500 عالدين
        var partial = await OkJsonAsync(await SellOnCreditAsync(cashier, productId, unitId, customerId, 1.000m), "بالدين جزئي");
        Assert.Equal(4.500m, partial.GetProperty("totalAmount").GetDecimal());

        // بلا ولا فلس (payments فاضية) → 4.500 كمان
        await OkJsonAsync(await SellOnCreditAsync(cashier, productId, unitId, customerId, 0m), "بالدين كامل");

        var after = await OkJsonAsync(await cashier.GetAsync("/api/v1/sales/credit-customer?phone=0791234567"), "الدين بعد");
        Assert.Equal(8.000m, after.GetProperty("currentDebt").GetDecimal());
        Assert.Equal(2, after.GetProperty("unpaidInvoiceCount").GetInt32());
    }

    [Fact]
    public async Task رقم_مش_مسجّل_404_ورقم_غلط_400_وبيع_ناقص_بلا_علم_الدين_مرفوض()
    {
        var (cashier, productId, unitId, customerId) = await SetUpAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await cashier.GetAsync("/api/v1/sales/credit-customer?phone=0770000001")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await cashier.GetAsync("/api/v1/sales/credit-customer?phone=abc")).StatusCode);

        // دفع ناقص بلا allowCreditSale = مرفوض زي قبل (ما في دين صامت)
        var silent = await SellOnCreditAsync(cashier, productId, unitId, customerId, 1.000m, allowCreditSale: false);
        Assert.False(silent.IsSuccessStatusCode);

        // allowCreditSale بلا زبون صريح = مرفوض
        var noCustomer = await SellOnCreditAsync(cashier, productId, unitId, customerId: null, 1.000m);
        Assert.False(noCustomer.IsSuccessStatusCode);
    }
}
