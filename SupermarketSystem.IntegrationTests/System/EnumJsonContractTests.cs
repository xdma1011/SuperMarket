using System.Net.Http.Json;
using System.Text.Json;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.SystemDomain;

/// <summary>
/// الباك إند بيسلسل كل الـenums كأسماء (JsonStringEnumConverter عام بـProgram.cs)،
/// ولوحة الإدارة كانت بصفحات كتير بتقارن بأرقام - المراجعات ما كانت تنحفظ، أزرار
/// الجرد ما كانت تبين، الإعدادات المنطقية تنعرض غلط، والتنبيهات دايمًا "غير مقروءة".
/// هالاختبار بيثبّت شكل الرد اللي الواجهة صارت تعتمد عليه.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class EnumJsonContractTests : IntegrationTestBase
{
    public EnumJsonContractTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task الـenums_بالردود_بتطلع_أسماء_مش_أرقام()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, _) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج عقد الـenums");
        await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, 1m);
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 3m);

        var client = await CreateAuthenticatedClientAsync();

        var created = await client.PostAsJsonAsync("/api/v1/stocktakes",
            new { branchId = Fixture.TestBranchId, includeAllProductsAtBranch = false, productIds = new[] { product.Id } });
        created.EnsureSuccessStatusCode();
        var stocktakeId = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("stocktakeId").GetGuid();

        var stocktake = await client.GetFromJsonAsync<JsonElement>($"/api/v1/stocktakes/{stocktakeId}");
        Assert.Equal("InProgress", stocktake.GetProperty("status").GetString());

        var settings = await client.GetFromJsonAsync<JsonElement>("/api/v1/system/admin-settings");
        var dataTypes = settings.GetProperty("settings").EnumerateArray().Select(s => s.GetProperty("dataType").GetString()).ToHashSet();
        Assert.Contains("Boolean", dataTypes);

        var products = await client.GetFromJsonAsync<JsonElement>("/api/v1/products?pageSize=200");
        Assert.All(products.GetProperty("items").EnumerateArray(),
            p => Assert.Equal(JsonValueKind.String, p.GetProperty("status").ValueKind));
    }
}
