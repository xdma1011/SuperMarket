using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Costing;
using SupermarketSystem.Domain.Purchasing;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Purchasing;

/// <summary>
/// رصيد افتتاحي (المراجعة النقدية 6/10/2026، بند 1): بضاعة موجودة قبل تشغيل النظام بتكلفتها، بلا مورد ولا دين.
/// بدونه كل بيعة منها بتنحسب ربح 100% وكشف الشركاء بيوزّع رأس مال كأنه ربح.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class OpeningBalanceTests : IntegrationTestBase
{
    public OpeningBalanceTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task رصيد_افتتاحي_بيزيد_المخزون_وبيغذّي_التكلفة_بلا_مورد_ولا_دين_ولا_دفعات()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        await TestDataBuilder.EnsureDocumentSequencesProvisionedAsync(db, Fixture.TestBranchId);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج رصيد افتتاحي");

        var client = await CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/api/v1/purchase-invoices/opening-balance", new
        {
            BranchId = Fixture.TestBranchId,
            Note = "جرد افتتاح المحل",
            Items = new[]
            {
                new
                {
                    ProductId = product.Id, ProductUnitId = unit.Id, Quantity = 10m, UnitCost = 2.500m,
                    ExistingProductBatchId = (Guid?)null, NewBatchNumber = (string?)null, NewBatchExpiryDate = (DateOnly?)null
                }
            }
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var invoiceId = created.GetProperty("purchaseInvoiceId").GetGuid();

        // المخزون زاد، والفاتورة بلا مورد ومعلّمة رصيد افتتاحي.
        var invoice = await db.PurchaseInvoices.AsNoTracking().SingleAsync(i => i.Id == invoiceId);
        Assert.True(invoice.IsOpeningBalance);
        Assert.Null(invoice.SupplierId);
        Assert.Equal(PurchaseInvoiceStatus.Received, invoice.Status);
        Assert.Equal(25.000m, invoice.TotalAmount);
        var onHand = await db.Stocks.AsNoTracking()
            .Where(s => s.ProductId == product.Id && s.BranchId == Fixture.TestBranchId).SumAsync(s => s.QuantityOnHand);
        Assert.Equal(10m, onHand);

        // التكلفة معروفة هلق (متوسط 2.500) - بيعة لاحقة بتاخد UnitCostSnapshot مش null.
        var averages = await PurchaseCostBasis.AverageBaseUnitCostsAsync(
            db, new[] { product.Id }, DateTime.UtcNow.AddMinutes(1), inclusive: true, CancellationToken.None);
        Assert.Equal(2.500m, averages[product.Id]);

        // بتظهر بقائمة الفواتير كرصيد افتتاحي (مش مختفية)، وبلا دين على أي مورد.
        var list = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/v1/purchase-invoices?pageSize=100");
        var row = list.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == invoiceId);
        Assert.True(row.GetProperty("isOpeningBalance").GetBoolean());
        Assert.Equal("رصيد افتتاحي", row.GetProperty("supplierName").GetString());
        var debts = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/v1/purchase-invoices/supplier-debts");
        Assert.DoesNotContain(debts.GetProperty("suppliers").EnumerateArray(), s => s.GetProperty("remainingDebt").GetDecimal() == 25.000m);

        // ما بتقبل دفعات مورد.
        var methodId = await db.PaymentMethods.AsNoTracking().Where(p => p.IsActive).Select(p => p.Id).FirstAsync();
        var pay = await client.PostAsJsonAsync($"/api/v1/purchase-invoices/{invoiceId}/payments", new
        {
            PaymentMethodId = methodId, Amount = 5m, ExternalReference = (string?)null, ClientRequestId = Guid.NewGuid()
        });
        Assert.Equal(HttpStatusCode.BadRequest, pay.StatusCode);
    }

    [Fact]
    public async Task رصيد_افتتاحي_لصاحب_المحل_فقط_مساعد_الأدمن_ممنوع_رغم_إنه_بيقدر_يسجّل_مشتريات()
    {
        using var scope = CreateScope();
        var db = CreateDbContext(scope);
        await TestDataBuilder.EnsureDocumentSequencesProvisionedAsync(db, Fixture.TestBranchId);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج رصيد افتتاحي ممنوع");

        var (_, username) = await UsersTestDataHelper.CreateUserWithRoleReturningUsernameAsync(
            Fixture.Factory.Services, Fixture.TestBranchId, UsersTestDataHelper.AssistantAdminRoleId, "opening.assistant");
        var assistant = await LoginHelper.LoginAsAsync(Fixture, username, UsersTestDataHelper.DefaultPassword);

        var response = await assistant.PostAsJsonAsync("/api/v1/purchase-invoices/opening-balance", new
        {
            BranchId = Fixture.TestBranchId,
            Items = new[]
            {
                new
                {
                    ProductId = product.Id, ProductUnitId = unit.Id, Quantity = 1m, UnitCost = 1m,
                    ExistingProductBatchId = (Guid?)null, NewBatchNumber = (string?)null, NewBatchExpiryDate = (DateOnly?)null
                }
            }
        });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
