using System.Net;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.ClientVersioning;

/// <summary>2-أ بند 2 (8/10/2026): نسخة كاشير أقدم من Cashier.MinimumVersion = 426 برسالة واضحة؛ بلا ترويسة نسخة أو إعداد فاضي = بلا فحص.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class ClientVersionTests : IntegrationTestBase
{
    public ClientVersionTests(DatabaseFixture fixture) : base(fixture) { }

    private static HttpRequestMessage Request(string? version)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/system/time-settings");
        if (version is not null)
        {
            request.Headers.Add("X-Client-App", "Cashier");
            request.Headers.Add("X-Client-Version", version);
        }

        return request;
    }

    private async Task SetMinimumAsync(string value)
    {
        using var scope = CreateScope();
        await TestDataBuilder.SetSettingAsync(scope, "Cashier.MinimumVersion", value);
    }

    [Fact]
    public async Task نسخة_كاشير_أقدم_من_الحد_الأدنى_بترجع_426_وغير_هيك_بتمرّ()
    {
        var client = Fixture.Factory.CreateClient();
        try
        {
            // إعداد فاضي = بلا فحص.
            await SetMinimumAsync("");
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Request("0.0.1"))).StatusCode);

            await SetMinimumAsync("1.2.0");

            var old = await client.SendAsync(Request("1.1.0"));
            Assert.Equal((HttpStatusCode)426, old.StatusCode);
            Assert.Contains("Client.UpdateRequired", await old.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Request("1.2.0"))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Request("2.0.0"))).StatusCode);

            // بلا ترويسة نسخة (لوحة الإدارة، كاشير قديم قبل الميزة): ما بينفحص.
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Request(null))).StatusCode);

            // ترويسة غير مفهومة: سماح.
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Request("abc"))).StatusCode);
        }
        finally
        {
            // كاش الإعدادات أطول عمرًا من تصفير القاعدة - نرجّعه فاضي عشان ما يأثر على اختبارات تانية.
            await SetMinimumAsync("");
        }
    }
}
