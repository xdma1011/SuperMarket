using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Reporting;

[Collection(DatabaseCollection.Name)]
public sealed class ReportingEndpointsHttpTests : IntegrationTestBase
{
    public ReportingEndpointsHttpTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task تقرير_المبيعات_الملغاة_بلا_توكن_يرجع_401()
    {
        var client = CreateAnonymousClient();

        var response = await client.GetAsync("/api/v1/reports/sales/voided");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task تقرير_المخزون_السالب_بتوكن_صحيح_يرجع_200()
    {
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.GetAsync($"/api/v1/reports/inventory/negative-stock?branchId={Fixture.TestBranchId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task تقرير_المبيعات_الملغاة_يرجع_سبب_الإلغاء_كنص_عربي_لا_رقم()
    {
        Guid saleInvoiceId;
        using (var scope = CreateScope())
        {
            var db = CreateDbContext(scope);
            var categoryId = await ReportingTestDataBuilder.CreateCategoryAsync(db, "تصنيف");
            var (productId, unitId) = await ReportingTestDataBuilder.CreateProductAsync(db, categoryId, "منتج");
            var invoice = await ReportingTestDataBuilder.CreateCompletedSaleAsync(db, Fixture.TestBranchId, productId, unitId, 1, 10m);
            await ReportingTestDataBuilder.VoidSaleAsync(db, invoice, Fixture.AdminUserId, SupermarketSystem.Domain.Sales.VoidReason.SystemError);
            saleInvoiceId = invoice.Id;
        }

        var client = await CreateAuthenticatedClientAsync();
        var response = await client.GetAsync($"/api/v1/reports/sales/voided?branchId={Fixture.TestBranchId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("items").EnumerateArray().ToList();
        var item = items.Single(i => i.GetProperty("saleInvoiceId").GetGuid() == saleInvoiceId);

        // JsonStringEnumConverter مفعَّل بـProgram.cs - لازم يرجع كنص
        // ("SystemError")، لا كرقم صامت (راجع CLAUDE.md §3.1).
        Assert.Equal("SystemError", item.GetProperty("voidReason").GetString());
    }
}
