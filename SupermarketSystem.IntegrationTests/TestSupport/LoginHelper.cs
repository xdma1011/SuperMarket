using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SupermarketSystem.Application.Authentication.Login;

namespace SupermarketSystem.IntegrationTests;

/// <summary>
/// نفس منطق DatabaseFixture.CreateAuthenticatedClientAsync بالضبط، بس
/// لأي مستخدم/دور تاني غير Master Admin الثابت - يخدم اختبارات صلاحيات
/// أضيق (403) وسيناريوهات أدوار مختلفة (كاشير/سائق/مساعد أدمن).
/// </summary>
internal static class LoginHelper
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<HttpClient> LoginAsAsync(
        DatabaseFixture fixture, string username, string password, string appType = "Admin", Guid? branchId = null)
    {
        var client = fixture.Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Username = username,
            Password = password,
            AppType = appType,
            BranchId = branchId ?? fixture.TestBranchId
        });

        response.EnsureSuccessStatusCode();
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }

    /// <summary>يرجع الاستجابة الخام بلا EnsureSuccessStatusCode - لاختبارات دخول متوقَّع فشلها (قفل، بيانات غلط...).</summary>
    public static Task<HttpResponseMessage> LoginRawAsync(
        DatabaseFixture fixture, string username, string password, string appType = "Admin", Guid? branchId = null)
    {
        var client = fixture.Factory.CreateClient();
        return client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Username = username,
            Password = password,
            AppType = appType,
            BranchId = branchId
        });
    }
}
