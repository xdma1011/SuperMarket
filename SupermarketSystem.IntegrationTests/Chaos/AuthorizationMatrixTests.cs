using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Domain.Identity;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;
using Xunit.Abstractions;

namespace SupermarketSystem.IntegrationTests.Chaos;

/// <summary>
/// مصفوفة صلاحيات عنيفة مبنية على مخطط Swagger (16/10/2026): بدل ما نكتب اختبار لكل endpoint يدويًا، بنمشي على <b>كل</b> العمليات بالنظام وبنضمن:
/// <list type="number">
/// <item>الزائر (بلا توكن) ما بيوصل لأي endpoint إلا اللي بالقائمة البيضاء الصريحة (<see cref="PublicOperations"/>) - endpoint جديد اننسى
///   عليه الحماية بيفشل هون فورًا بدل ما يتكشف بعد الرفع. والقائمة نفسها لازم تضل صادقة (واحد منها صار محمي = فشل).</item>
/// <item>مستخدم مسجّل دخول <b>بلا ولا صلاحية</b> ما بيوصل لأي endpoint إلا اللي بقائمة "يكفي تسجيل الدخول" (<see cref="AuthenticatedOnlyOperations"/>)
///   - هاد بيصطاد endpoint نُسي عليه <c>RequirePermission</c>.</item>
/// <item>صلاحيات الأدوار الثابتة (كاشير/سائق/مساعد أدمن) <b>مثبّتة حرفيًا</b>: أي تغيير بصلاحيات دور بيفشل الاختبار ويجبرك تقرّر عن قصد،
///   وقواعد صاحب المشروع (الكاشير ممنوع من المالية والتقارير والشركاء والموظفين والتنبيهات...) مفحوصة صراحةً.</item>
/// </list>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class AuthorizationMatrixTests : IntegrationTestBase
{
    private readonly ITestOutputHelper _output;
    public AuthorizationMatrixTests(DatabaseFixture fixture, ITestOutputHelper output) : base(fixture) { _output = output; }

    private static readonly string[] SkipPathFragments =
    {
        "/backups", "/auth/logout", "/auth/sessions", "/auth/login", "/auth/refresh", "bootstrap-admin", "/swagger", "/health"
    };

    /// <summary>endpoints مفتوحة للجميع بقرار (تطبيق الزبائن، تلغرام، الفروع لشاشة الدخول...). أي إضافة هون = قرار أمني واعي.</summary>
    private static readonly HashSet<string> PublicOperations = new(StringComparer.Ordinal)
    {
        "GET /api/v1/auth/branches",
        "GET /api/v1/customers/{customerId}/coupons",
        "POST /api/v1/customers/{customerId}/coupons/preview",
        "POST /api/v1/customer-auth/request-otp",
        "POST /api/v1/customer-auth/verify-otp",
        "POST /api/v1/telegram/webhook",
        "POST /api/v1/complaints",
        "GET /api/v1/customers/{customerId}/qr-token",
        "POST /api/v1/customers/{customerId}/device-tokens",
        "GET /api/v1/customers/{customerId}/loyalty-balance",
        "POST /api/v1/orders",
        "GET /api/v1/orders/customers/{customerId}",
        "POST /api/v1/orders/{orderId}/rate",
        "GET /api/v1/orders/{orderId}/customer-view",
        "GET /api/v1/catalog/products",
        "GET /api/v1/catalog/categories",
        "GET /api/v1/system/time-settings",
    };

    /// <summary>endpoints بتكفي فيها هوية المستخدم المسجَّل (بلا صلاحية معيّنة): بيانات مرجعية/هويته هو.</summary>
    private static readonly HashSet<string> AuthenticatedOnlyOperations = new(StringComparer.Ordinal)
    {
        "GET /api/v1/auth/my-permissions",
        "GET /api/v1/currencies",
        "GET /api/v1/common-units",
        "GET /api/v1/payment-methods",
    };

    private static string Key(SwaggerModel.Operation op) => $"{op.Method} {op.Path}";

    private async Task<(SwaggerModel Model, List<SwaggerModel.Operation> Ops)> LoadAsync()
    {
        var admin = await CreateAuthenticatedClientAsync();
        var swagger = await admin.GetAsync("/swagger/v1/swagger.json");
        Assert.True(swagger.IsSuccessStatusCode);
        var model = new SwaggerModel((JsonObject)JsonNode.Parse(await swagger.Content.ReadAsStringAsync())!);
        var ops = model.LoadOperations()
            .Where(o => !SkipPathFragments.Any(f => o.Path.Contains(f, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        Assert.True(ops.Count > 100);
        return (model, ops);
    }

    private static HttpRequestMessage Build(SwaggerModel model, SwaggerModel.Operation op)
    {
        var req = new HttpRequestMessage(new HttpMethod(op.Method), model.FillPath(op));
        if (op.Method == "GET") return req;
        if (op.Multipart)
        {
            var form = new MultipartFormDataContent { { new ByteArrayContent([1, 2, 3]), "file", "x.jpg" } };
            req.Content = form;
        }
        else
        {
            req.Content = new StringContent(model.Sample(op.BodySchema)?.ToJsonString() ?? "{}", Encoding.UTF8, "application/json");
        }

        return req;
    }

    private async Task<HttpClient> UserClientAsync(Guid roleId, string prefix, string appType = "Admin")
    {
        var (_, name) = await UsersTestDataHelper.CreateUserWithRoleReturningUsernameAsync(Fixture.Factory.Services, Fixture.TestBranchId, roleId, prefix);
        return await LoginHelper.LoginAsAsync(Fixture, name, UsersTestDataHelper.DefaultPassword, appType);
    }

    private async Task<Guid> CreateRoleWithoutPermissionsAsync()
    {
        using var scope = CreateScope();
        var db = CreateDbContext(scope);
        var role = new Role($"بلا صلاحيات {Guid.NewGuid():N}", "اختبار المصفوفة");
        db.Roles.Add(role);
        await db.SaveChangesAsync();
        return role.Id;
    }

    [Fact]
    public async Task الزائر_بلا_توكن_ما_بيوصل_لأي_endpoint_إلا_القائمة_البيضاء_الصريحة()
    {
        var (model, ops) = await LoadAsync();
        var anon = CreateAnonymousClient();
        var leaked = new List<string>();
        var staleWhitelist = new List<string>();
        var seenPublic = new HashSet<string>();

        foreach (var op in ops)
        {
            using var req = Build(model, op);
            var status = (await anon.SendAsync(req)).StatusCode;
            var isPublic = PublicOperations.Contains(Key(op));
            if (isPublic) seenPublic.Add(Key(op));
            if (!isPublic && status != HttpStatusCode.Unauthorized) leaked.Add($"{(int)status} {Key(op)}");
            if (isPublic && status == HttpStatusCode.Unauthorized) staleWhitelist.Add(Key(op));
        }

        Assert.True(leaked.Count == 0, "endpoints بتستجيب للزائر بلا قرار صريح (نسيت RequirePermission؟):\n" + string.Join('\n', leaked));
        Assert.True(staleWhitelist.Count == 0, "القائمة البيضاء فيها endpoints صارت محمية - شيلها منها:\n" + string.Join('\n', staleWhitelist));
        var missing = PublicOperations.Except(seenPublic).ToList();
        Assert.True(missing.Count == 0, "القائمة البيضاء فيها endpoints مش موجودة بالمخطط:\n" + string.Join('\n', missing));
    }

    [Fact]
    public async Task مستخدم_مسجّل_بلا_ولا_صلاحية_ممنوع_من_كل_endpoint_إلا_العامة_ويكفي_فيها_تسجيل_الدخول()
    {
        var (model, ops) = await LoadAsync();
        var roleId = await CreateRoleWithoutPermissionsAsync();
        var client = await UserClientAsync(roleId, "matrix.none");
        var offenders = new List<string>();
        var staleAuthOnly = new HashSet<string>(AuthenticatedOnlyOperations);

        foreach (var op in ops)
        {
            if (PublicOperations.Contains(Key(op))) continue;
            using var req = Build(model, op);
            var response = await client.SendAsync(req);
            var key = Key(op);
            if (AuthenticatedOnlyOperations.Contains(key))
            {
                staleAuthOnly.Remove(key);
                if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized) offenders.Add($"{(int)response.StatusCode} {key} (المفروض يكفي تسجيل الدخول)");
                continue;
            }

            var text = await response.Content.ReadAsStringAsync();
            if (response.StatusCode != HttpStatusCode.Forbidden || !text.Contains("لا تملك صلاحية"))
                offenders.Add($"{(int)response.StatusCode} {key} (المفروض 403 'لا تملك صلاحية')");
        }

        Assert.True(offenders.Count == 0, "endpoints بتشتغل لمستخدم بلا صلاحيات:\n" + string.Join('\n', offenders));
        Assert.True(staleAuthOnly.Count == 0, "قائمة 'يكفي تسجيل الدخول' فيها endpoints مش موجودة:\n" + string.Join('\n', staleAuthOnly));
    }

    private async Task<SortedSet<string>> MyPermissionsAsync(HttpClient client)
    {
        var json = JsonDocument.Parse(await client.GetStringAsync("/api/v1/auth/my-permissions")).RootElement;
        var set = new SortedSet<string>(StringComparer.Ordinal);
        var arr = json.ValueKind == JsonValueKind.Array ? json : json.EnumerateObject().First(p => p.Value.ValueKind == JsonValueKind.Array).Value;
        foreach (var item in arr.EnumerateArray()) set.Add(item.ValueKind == JsonValueKind.String ? item.GetString()! : item.GetProperty("code").GetString()!);
        return set;
    }

    [Fact]
    public async Task صلاحيات_الأدوار_الثابتة_مثبّتة_حرفيًا_وقواعد_صاحب_المشروع_محترمة()
    {
        var cashier = await MyPermissionsAsync(await UserClientAsync(UsersTestDataHelper.CashierRoleId, "matrix.cashier", "Cashier"));
        var driver = await MyPermissionsAsync(await UserClientAsync(UsersTestDataHelper.DriverRoleId, "matrix.driver"));
        var assistant = await MyPermissionsAsync(await UserClientAsync(UsersTestDataHelper.AssistantAdminRoleId, "matrix.assistant"));
        var master = await MyPermissionsAsync(await UserClientAsync(UsersTestDataHelper.MasterAdminRoleId, "matrix.master"));

        // لقطة حرفية: أي تغيير بصلاحيات دور ثابت لازم يكون قرار واعي (يعدّل هالقوائم بنفس الـcommit).
        Assert.Equal(new[] { "CashClosing.Manage", "Purchasing.CreateDraft", "Returns.Process", "Sales.Create", "Sales.Void" }, cashier.ToArray());
        Assert.Equal(new[] { "Orders.Deliver" }, driver.ToArray());
        Assert.Equal(new[]
        {
            "CashClosing.Manage", "Catalog.ChangePriceDirect", "Catalog.Manage", "Catalog.RequestPriceChange", "Customers.Manage", "Notifications.Manage",
            "Notifications.View", "Purchasing.Create", "Purchasing.CreateDraft", "Reports.View", "Returns.Process", "Returns.Review", "Sales.Create", "Sales.Void",
            "StockTransfer.Manage", "Stocktake.Approve", "Stocktake.Manage", "Suppliers.Manage"
        }, assistant.ToArray());

        // قواعد صاحب المشروع (CLAUDE.md §5.2): كاشير ممنوع من المالية/التقارير/الشركاء/التنبيهات/الإدارة؛ Finance.Manage وPartners.Manage للماستر حصرًا.
        string[] forbiddenForCashier = ["Finance.", "Partners.", "Reports.", "Notifications.", "Users.", "Backups.", "Branches.", "System.", "Sessions.", "Inventory.", "Sales.AtCost"];
        Assert.DoesNotContain(cashier, c => forbiddenForCashier.Any(f => c.StartsWith(f, StringComparison.Ordinal)));
        Assert.DoesNotContain("Finance.Manage", assistant);
        Assert.DoesNotContain("Partners.Manage", assistant);
        Assert.Contains("Finance.Manage", master);
        Assert.Contains("Partners.Manage", master);

        // الماستر لازم يملك كل صلاحية معرّفة بالنظام (صلاحية جديدة انضافت بلا ما تنمنح للماستر = أدمن مقفول عنها).
        using var scope = CreateScope();
        var db = CreateDbContext(scope);
        var all = await db.Set<Permission>().AsNoTracking().Select(p => p.Code).ToListAsync();
        Assert.Empty(all.Except(master));
    }
}
