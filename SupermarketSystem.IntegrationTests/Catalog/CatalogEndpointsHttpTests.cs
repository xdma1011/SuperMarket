using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Catalog;

/// <summary>اختبار HTTP كامل لمجموعة CatalogEndpoints (المسار الفعلي، لا استدعاء Handler مباشر).</summary>
[Collection(DatabaseCollection.Name)]
public sealed class CatalogEndpointsHttpTests : IntegrationTestBase
{
    public CatalogEndpointsHttpTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task جلب_المنتجات_بلا_توكن_يرجع_401()
    {
        var client = CreateAnonymousClient();

        var response = await client.GetAsync("/api/v1/products");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task إنشاء_تصنيف_ثم_منتج_عبر_HTTP_يرجع_201()
    {
        var client = await CreateAuthenticatedClientAsync();

        var categoryResponse = await client.PostAsJsonAsync("/api/v1/product-categories", new
        {
            Name = "تصنيف عبر HTTP",
            ParentCategoryId = (Guid?)null
        });
        Assert.Equal(HttpStatusCode.Created, categoryResponse.StatusCode);
        var category = await categoryResponse.Content.ReadFromJsonAsync<CategoryResponse>();

        var productResponse = await client.PostAsJsonAsync("/api/v1/products", new
        {
            Name = "منتج عبر HTTP",
            Description = (string?)null,
            CategoryId = category!.CategoryId,
            IsBatchTracked = false,
            SuggestedRetailPrice = (decimal?)null,
            ExpectedShelfLifeDays = (int?)null,
            Units = new[] { new { UnitName = "قطعة", ConversionFactorToBase = 1m, IsBaseUnit = true } },
            Barcodes = Array.Empty<object>()
        });

        Assert.Equal(HttpStatusCode.Created, productResponse.StatusCode);
        Assert.NotNull(productResponse.Headers.Location);
    }

    private sealed record CategoryResponse(Guid CategoryId, string Name);
}
