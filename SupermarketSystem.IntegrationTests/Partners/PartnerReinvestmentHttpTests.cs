using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Domain.Notifications;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Partners;

/// <summary>
/// إعادة استثمار ربح الشريك برأس ماله (16/10/2026) عبر HTTP حقيقي، بأرقام محسوبة مسبقًا:
///   الشهر الماضي: بيع 10 حليب × 1.250 = 12.500، تكلفة 10 × 0.800 = 8.000 → صافي 4.500.
///   أحمد وسامي رأس مال 10 و10 → نصيب كل واحد 2.250 (50%).
///   أحمد بيعيد استثمار 2.000 → رأس ماله 12، رصيده 0.250، نسبته 12/22 = 54.55% (من 50%).
///   ولو الإيداع الناتج كان قبل نهاية الشهر الماضي: إعادة إصدار الكشف (والمضارب 5% = 0.225) بتعطي أحمد 4.275×12/22 = 2.332 وسامي 1.943.
/// الحماية: كلمة سر المستخدم الحالي (مش أي حساب)، قفل بعد محاولات فاشلة، idempotency، رفض المبلغ الزايد/المضارب، وسباق متزامن.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PartnerReinvestmentHttpTests : IntegrationTestBase
{
    public PartnerReinvestmentHttpTests(DatabaseFixture fixture) : base(fixture) { }

    private static readonly JsonSerializerOptions StringEnums = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response, string what)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{what}: {(int)response.StatusCode} {body}");
        return string.IsNullOrWhiteSpace(body) ? default : JsonDocument.Parse(body).RootElement;
    }

    private static DateTime PreviousMonthMiddle()
    {
        var now = DateTime.UtcNow;
        return new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-1).AddDays(14);
    }

    private sealed record Setup(HttpClient Admin, Guid Ahmad, Guid Sami, Guid Mudarib, DateTime Previous);

    /// <summary>أحمد وسامي رأس مال 10/10 + مضارب 0%، وكشف الشهر الماضي نازل (نصيب كل واحد من الرأسماليين 2.250).</summary>
    private async Task<Setup> SetUpAsync()
    {
        var branchId = Fixture.TestBranchId;
        using (var scope = CreateScope())
        {
            await TestDataBuilder.EnsureDocumentSequencesProvisionedAsync(CreateDbContext(scope), branchId);
        }

        var admin = await CreateAuthenticatedClientAsync();
        var category = await ReadJsonAsync(await admin.PostAsJsonAsync("/api/v1/product-categories", new { name = "ألبان", parentCategoryId = (Guid?)null }), "تصنيف");
        var created = await ReadJsonAsync(await admin.PostAsJsonAsync("/api/v1/products", new
        {
            name = "حليب", description = (string?)null, categoryId = category.GetProperty("categoryId").GetGuid(), isBatchTracked = false,
            suggestedRetailPrice = (decimal?)null, expectedShelfLifeDays = (int?)null,
            units = new[] { new { unitName = "حبة", conversionFactorToBase = 1m, isBaseUnit = true } },
            barcodes = Array.Empty<object>()
        }), "منتج");
        var milkId = created.GetProperty("productId").GetGuid();
        await ReadJsonAsync(await admin.PostAsJsonAsync($"/api/v1/products/{milkId}/branches",
            new { branchId, sellingPrice = 1.250m, minimumStock = 0m, maximumStock = (decimal?)null }), "ربط");
        var milkUnit = (await ReadJsonAsync(await admin.GetAsync($"/api/v1/products/{milkId}/units"), "وحدات")).EnumerateArray().Single().GetProperty("id").GetGuid();
        var supplier = await ReadJsonAsync(await admin.PostAsJsonAsync("/api/v1/suppliers", new
        {
            name = "مورد", contactName = (string?)null, phone = "0790000001", email = (string?)null,
            street = (string?)null, city = "عمّان", postalCode = (string?)null, country = "الأردن"
        }), "مورد");
        await ReadJsonAsync(await admin.PostAsJsonAsync("/api/v1/purchase-invoices", new
        {
            branchId, supplierId = supplier.GetProperty("supplierId").GetGuid(), supplierInvoiceReference = "P-1",
            items = new[] { new { productId = milkId, productUnitId = milkUnit, quantity = 50m, unitCost = 0.800m, existingProductBatchId = (Guid?)null, newBatchNumber = (string?)null, newBatchExpiryDate = (DateOnly?)null } },
            imageReferences = (string[]?)null, dueDate = (DateOnly?)null
        }), "شراء");

        var previous = PreviousMonthMiddle();
        await ReadJsonAsync(await admin.PostAsJsonAsync("/api/v1/sales", new
        {
            branchId, clientRequestId = Guid.NewGuid(), customerId = (Guid?)null, invoiceLevelDiscountAmount = 0m,
            items = new[] { new { productId = milkId, productUnitId = milkUnit, quantity = 10m, manualDiscountAmount = 0m, productBatchId = (Guid?)null } },
            payments = new[] { new { paymentMethodId = TestDataBuilder.CashPaymentMethodId, amount = 12.500m, externalReference = (string?)null, clientRequestId = Guid.NewGuid() } }
        }), "بيع");
        using (var scope = CreateScope())
        {
            await CreateDbContext(scope).SaleInvoices.IgnoreQueryFilters().ExecuteUpdateAsync(x => x.SetProperty(i => i.CreatedAtUtc, previous));
        }

        async Task<Guid> PartnerAsync(string name, string type, decimal? percent) => (await ReadJsonAsync(await admin.PostAsJsonAsync("/api/v1/partners", new
        {
            branchId, fullName = name, type, userId = (Guid?)null, speculativeProfitPercent = percent, notes = (string?)null
        }, StringEnums), $"شريك {name}")).GetProperty("partnerId").GetGuid();

        var ahmad = await PartnerAsync("أحمد", "Capital", null);
        var sami = await PartnerAsync("سامي", "Capital", null);
        foreach (var p in new[] { ahmad, sami })
        {
            await ReadJsonAsync(await admin.PostAsJsonAsync("/api/v1/finance/capital-transactions", new
            {
                branchId, type = "Deposit", amount = 10m, occurredAtUtc = previous.AddDays(-5), notes = "رأس مال", partnerId = p
            }, StringEnums), "رأس مال");
        }

        await ReadJsonAsync(await admin.PostAsJsonAsync("/api/v1/partners/statements", new { branchId, year = previous.Year, month = previous.Month }), "كشف");
        // المضارب بينضاف بعد نزول الكشف عشان ما يغيّر أنصبته (2.250 لكل رأسمالي).
        var mudarib = await PartnerAsync("خالد المضارب", "Speculative", 5m);
        return new Setup(admin, ahmad, sami, mudarib, previous);
    }

    private static Task<HttpResponseMessage> ReinvestAsync(HttpClient client, Guid partnerId, decimal amount, string? password, Guid? requestId = null, string? notes = null) =>
        client.PostAsJsonAsync($"/api/v1/partners/{partnerId}/reinvest", new { amount, password, notes, clientRequestId = requestId ?? Guid.NewGuid() });

    private async Task<JsonElement> PartnerAsync(HttpClient admin, Guid id) =>
        (await ReadJsonAsync(await admin.GetAsync($"/api/v1/partners?branchId={Fixture.TestBranchId}"), "الشركاء"))
        .EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == id);

    [Fact]
    public async Task إعادة_استثمار_بكلمة_سر_صحيحة_بتنقص_الرصيد_وتزيد_رأس_المال_والنسبة_بلا_حركة_نقد()
    {
        var s = await SetUpAsync();
        var before = await PartnerAsync(s.Admin, s.Ahmad);
        Assert.Equal(2.250m, before.GetProperty("currentBalance").GetDecimal());
        Assert.Equal(10m, before.GetProperty("capitalBalance").GetDecimal());

        int drawerLogsBefore;
        using (var scope = CreateScope())
        {
            drawerLogsBefore = await CreateDbContext(scope).CashDrawerLogs.IgnoreQueryFilters().CountAsync();
        }

        // معاينة بلا أي كتابة، والأرقام نفسها اللي بتتنفذ.
        var preview = await ReadJsonAsync(await s.Admin.GetAsync($"/api/v1/partners/{s.Ahmad}/reinvest-preview?amount=2.000"), "معاينة");
        Assert.True(preview.GetProperty("canReinvest").GetBoolean());
        Assert.Equal(50.00m, preview.GetProperty("sharePercentBefore").GetDecimal());
        Assert.Equal(54.55m, preview.GetProperty("sharePercentAfter").GetDecimal());
        Assert.Equal(0.250m, preview.GetProperty("balanceAfter").GetDecimal());
        Assert.Equal(2.250m, (await PartnerAsync(s.Admin, s.Ahmad)).GetProperty("currentBalance").GetDecimal());

        var response = await ReadJsonAsync(await ReinvestAsync(s.Admin, s.Ahmad, 2.000m, DatabaseFixture.AdminPassword, notes: "توسعة"), "إعادة استثمار");
        Assert.False(response.GetProperty("wasReplay").GetBoolean());
        Assert.Equal(0.250m, response.GetProperty("newBalance").GetDecimal());
        Assert.Equal(10m, response.GetProperty("capitalBefore").GetDecimal());
        Assert.Equal(12m, response.GetProperty("capitalAfter").GetDecimal());
        Assert.Equal(50.00m, response.GetProperty("sharePercentBefore").GetDecimal());
        Assert.Equal(54.55m, response.GetProperty("sharePercentAfter").GetDecimal());

        var after = await PartnerAsync(s.Admin, s.Ahmad);
        Assert.Equal(0.250m, after.GetProperty("currentBalance").GetDecimal());
        Assert.Equal(12m, after.GetProperty("capitalBalance").GetDecimal());
        Assert.Equal(2.000m, after.GetProperty("reinvestedTotal").GetDecimal());
        Assert.Equal(10m, (await PartnerAsync(s.Admin, s.Sami)).GetProperty("capitalBalance").GetDecimal()); // الشريك التاني ما تأثر

        // كشف حساب الشريك: حركة "إعادة استثمار" سالبة والرصيد المتراكم صح.
        var ledger = await ReadJsonAsync(await s.Admin.GetAsync($"/api/v1/partners/{s.Ahmad}/ledger"), "كشف حساب");
        var entry = ledger.GetProperty("entries").EnumerateArray().Single(e => e.GetProperty("kindCode").GetString() == "reinvest");
        Assert.Equal(-2.000m, entry.GetProperty("amount").GetDecimal());
        Assert.Equal(0.250m, ledger.GetProperty("currentBalance").GetDecimal());

        using (var scope = CreateScope())
        {
            var db = CreateDbContext(scope);
            Assert.Equal(drawerLogsBefore, await db.CashDrawerLogs.IgnoreQueryFilters().CountAsync()); // بلا حركة نقد
            var capital = await db.CapitalTransactions.IgnoreQueryFilters().AsNoTracking()
                .Where(c => c.PartnerId == s.Ahmad && c.Amount == 2.000m).SingleAsync();
            var reinvestment = await db.PartnerReinvestments.IgnoreQueryFilters().AsNoTracking().SingleAsync();
            Assert.Equal(capital.Id, reinvestment.CapitalTransactionId);
            Assert.Equal("توسعة", reinvestment.Notes);
            var severities = await db.Notifications.IgnoreQueryFilters().AsNoTracking()
                .Where(n => n.Title.Contains("إعادة استثمار")).Select(n => n.Severity).ToListAsync();
            Assert.NotEmpty(severities); // تنبيه "مهم" لصاحب المحل (واحد لكل مستلم)
            Assert.All(severities, sev => Assert.Equal(NotificationSeverity.Warning, sev));
        }
    }

    [Fact]
    public async Task رأس_المال_الجديد_بيغيّر_توزيع_الأرباح_بالكشف_حسب_النسبة_الجديدة()
    {
        var s = await SetUpAsync();
        await ReadJsonAsync(await ReinvestAsync(s.Admin, s.Ahmad, 2.000m, DatabaseFixture.AdminPassword), "إعادة استثمار");

        // نرجّع تاريخ الإيداع الناتج لقبل نهاية الشهر الماضي ونعيد إصدار كشفه: رأس المال 12 مقابل 10 = 54.55%/45.45%.
        using (var scope = CreateScope())
        {
            await CreateDbContext(scope).CapitalTransactions.IgnoreQueryFilters()
                .Where(c => c.PartnerId == s.Ahmad && c.Amount == 2.000m)
                .ExecuteUpdateAsync(x => x.SetProperty(c => c.OccurredAtUtc, s.Previous.AddDays(-2)));
        }

        var statement = await ReadJsonAsync(await s.Admin.PostAsJsonAsync("/api/v1/partners/statements",
            new { branchId = Fixture.TestBranchId, year = s.Previous.Year, month = s.Previous.Month }), "إعادة إصدار");
        var lines = statement.GetProperty("lines").EnumerateArray().ToDictionary(l => l.GetProperty("partnerId").GetGuid());
        Assert.Equal(12m, lines[s.Ahmad].GetProperty("capitalBalance").GetDecimal());
        // المضارب (5%) انضاف بعد الكشف الأول: 4.500 × 5% = 0.225، والباقي 4.275 بين الرأسماليين بنسبة 12:10.
        Assert.Equal(0.225m, lines[s.Mudarib].GetProperty("shareAmount").GetDecimal());
        Assert.Equal(2.332m, lines[s.Ahmad].GetProperty("shareAmount").GetDecimal());
        Assert.Equal(1.943m, lines[s.Sami].GetProperty("shareAmount").GetDecimal());
        Assert.Equal(4.500m, lines.Values.Sum(l => l.GetProperty("shareAmount").GetDecimal()));
    }

    [Fact]
    public async Task كلمة_سر_غلط_أو_فاضية_بترفض_وما_بينكتب_ولا_شي()
    {
        var s = await SetUpAsync();
        foreach (var bad in new string?[] { "غلط", "", null, "  " })
        {
            var response = await ReinvestAsync(s.Admin, s.Ahmad, 1.000m, bad);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        // كلمة سر حساب تاني (كاشير) برضو ما بتنفع - لازم كلمة المستخدم الحالي نفسه.
        var (_, cashierName) = await UsersTestDataHelper.CreateUserWithRoleReturningUsernameAsync(
            Fixture.Factory.Services, Fixture.TestBranchId, UsersTestDataHelper.CashierRoleId, "reinvest.other");
        Assert.NotEqual(DatabaseFixture.AdminPassword, UsersTestDataHelper.DefaultPassword);
        Assert.Equal(HttpStatusCode.Forbidden, (await ReinvestAsync(s.Admin, s.Ahmad, 1.000m, UsersTestDataHelper.DefaultPassword)).StatusCode);
        Assert.False(string.IsNullOrEmpty(cashierName));

        var partner = await PartnerAsync(s.Admin, s.Ahmad);
        Assert.Equal(2.250m, partner.GetProperty("currentBalance").GetDecimal());
        Assert.Equal(10m, partner.GetProperty("capitalBalance").GetDecimal());
        using var scope = CreateScope();
        Assert.Equal(0, await CreateDbContext(scope).PartnerReinvestments.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task محاولات_كلمة_سر_فاشلة_متكررة_بتقفل_الحساب_حتى_لو_الكلمة_الصح_بعدها()
    {
        var s = await SetUpAsync();
        var masterId = await UsersTestDataHelper.CreateUserWithRoleAsync(
            Fixture.Factory.Services, Fixture.TestBranchId, UsersTestDataHelper.MasterAdminRoleId, "reinvest.master");
        string username;
        using (var scope = CreateScope())
        {
            username = (await CreateDbContext(scope).Users.IgnoreQueryFilters().AsNoTracking().SingleAsync(u => u.Id == masterId)).Username;
        }

        var master = await LoginHelper.LoginAsAsync(Fixture, username, UsersTestDataHelper.DefaultPassword);
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await ReinvestAsync(master, s.Ahmad, 1.000m, "غلط")).StatusCode);
        }

        var locked = await ReinvestAsync(master, s.Ahmad, 1.000m, UsersTestDataHelper.DefaultPassword);
        Assert.Equal(HttpStatusCode.Forbidden, locked.StatusCode);
        Assert.Contains("مقفل", await locked.Content.ReadAsStringAsync());
        using var check = CreateScope();
        Assert.Equal(0, await CreateDbContext(check).PartnerReinvestments.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task غلطات_كلمة_السر_القديمة_برا_نافذة_القفل_ما_بتتراكم_مع_الجديدة()
    {
        var s = await SetUpAsync();
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await ReinvestAsync(s.Admin, s.Ahmad, 1.000m, "غلط")).StatusCode);
        }

        // الأربع غلطات صارت من ساعة، وغلطة خامسة جديدة ما بتكمّل قفل (نافذة 15 دقيقة) - والكلمة الصح بعدها بتنفع.
        using (var scope = CreateScope())
        {
            await CreateDbContext(scope).UserLoginLogs.IgnoreQueryFilters()
                .Where(l => !l.Success)
                .ExecuteUpdateAsync(x => x.SetProperty(l => l.AttemptedAtUtc, DateTime.UtcNow.AddHours(-1)));
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await ReinvestAsync(s.Admin, s.Ahmad, 1.000m, "غلط")).StatusCode);
        var ok = await ReinvestAsync(s.Admin, s.Ahmad, 1.000m, DatabaseFixture.AdminPassword);
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
    }

    /// <summary>
    /// ما في تراجع جاهز (قرار نهائي بالتصميم). المسار الوحيد المتاح حاليًا = سحب رأس مال من المالية، وهو بينقص رأس المال بس
    /// <b>ما بيرجّع الرصيد</b> - الاختبار بيثبّت هالحد عشان ما حدا يفترض عكس ذلك.
    /// </summary>
    [Fact]
    public async Task سحب_رأس_مال_من_المالية_بعد_إعادة_الاستثمار_بينقص_رأس_المال_بس_وما_بيرجّع_الرصيد()
    {
        var s = await SetUpAsync();
        await ReadJsonAsync(await ReinvestAsync(s.Admin, s.Ahmad, 2.000m, DatabaseFixture.AdminPassword), "إعادة استثمار");

        await ReadJsonAsync(await s.Admin.PostAsJsonAsync("/api/v1/finance/capital-transactions", new
        {
            branchId = Fixture.TestBranchId, type = "Withdrawal", amount = 2.000m, occurredAtUtc = DateTime.UtcNow, notes = "تراجع", partnerId = s.Ahmad
        }, StringEnums), "سحب رأس مال");

        var partner = await PartnerAsync(s.Admin, s.Ahmad);
        Assert.Equal(10m, partner.GetProperty("capitalBalance").GetDecimal());
        Assert.Equal(0.250m, partner.GetProperty("currentBalance").GetDecimal()); // الرصيد ما رجع
    }

    [Fact]
    public async Task مبلغ_أكبر_من_الرصيد_أو_صفر_أو_شريك_مضارب_أو_موقوف_أو_رصيد_سالب_مرفوض()
    {
        var s = await SetUpAsync();
        var tooMuch = await ReinvestAsync(s.Admin, s.Ahmad, 2.251m, DatabaseFixture.AdminPassword);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooMuch.StatusCode);
        Assert.Contains("PartnerReinvest.ExceedsBalance", await tooMuch.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await ReinvestAsync(s.Admin, s.Ahmad, 0m, DatabaseFixture.AdminPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ReinvestAsync(s.Admin, s.Ahmad, -5m, DatabaseFixture.AdminPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await ReinvestAsync(s.Admin, s.Mudarib, 1.000m, DatabaseFixture.AdminPassword)).StatusCode);

        // سامي بيسحب أكتر من رصيده -> رصيد سالب -> ما في شي يعاد استثماره.
        await ReadJsonAsync(await s.Admin.PostAsJsonAsync("/api/v1/partners/withdrawals", new
        {
            partnerId = s.Sami, amount = 5.000m, source = "Drawer", notes = (string?)null, occurredAtUtc = (DateTime?)null, clientRequestId = Guid.NewGuid()
        }, StringEnums), "سحب");
        var negative = await ReinvestAsync(s.Admin, s.Sami, 1.000m, DatabaseFixture.AdminPassword);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, negative.StatusCode);
        Assert.Contains("PartnerReinvest.NoBalance", await negative.Content.ReadAsStringAsync());

        await ReadJsonAsync(await s.Admin.PostAsJsonAsync($"/api/v1/partners/{s.Ahmad}/active", new { isActive = false }), "إيقاف");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await ReinvestAsync(s.Admin, s.Ahmad, 1.000m, DatabaseFixture.AdminPassword)).StatusCode);

        // المعاينة بتبيّن السبب بدل ما ترمي.
        var preview = await ReadJsonAsync(await s.Admin.GetAsync($"/api/v1/partners/{s.Mudarib}/reinvest-preview?amount=1"), "معاينة مضارب");
        Assert.False(preview.GetProperty("canReinvest").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(preview.GetProperty("blockedReason").GetString()));
        using var scope = CreateScope();
        Assert.Equal(0, await CreateDbContext(scope).PartnerReinvestments.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task ضغطتين_بنفس_المفتاح_بتنفذ_مرة_وحدة_والتانية_إعادة_بلا_كلمة_سر()
    {
        var s = await SetUpAsync();
        var requestId = Guid.NewGuid();
        var first = await ReinvestAsync(s.Admin, s.Ahmad, 1.000m, DatabaseFixture.AdminPassword, requestId);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var second = await ReinvestAsync(s.Admin, s.Ahmad, 1.000m, "مش مهم", requestId);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.True(JsonDocument.Parse(await second.Content.ReadAsStringAsync()).RootElement.GetProperty("wasReplay").GetBoolean());

        var partner = await PartnerAsync(s.Admin, s.Ahmad);
        Assert.Equal(1.250m, partner.GetProperty("currentBalance").GetDecimal());
        Assert.Equal(11m, partner.GetProperty("capitalBalance").GetDecimal());
    }

    [Fact]
    public async Task مساعد_الأدمن_والكاشير_ممنوعين_من_إعادة_الاستثمار()
    {
        var s = await SetUpAsync();
        foreach (var (roleId, app) in new[] { (UsersTestDataHelper.AssistantAdminRoleId, "Admin"), (UsersTestDataHelper.CashierRoleId, "Cashier") })
        {
            var (_, name) = await UsersTestDataHelper.CreateUserWithRoleReturningUsernameAsync(Fixture.Factory.Services, Fixture.TestBranchId, roleId, "reinvest.denied");
            var client = await LoginHelper.LoginAsAsync(Fixture, name, UsersTestDataHelper.DefaultPassword, app);
            Assert.Equal(HttpStatusCode.Forbidden, (await ReinvestAsync(client, s.Ahmad, 1.000m, UsersTestDataHelper.DefaultPassword)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/v1/partners/{s.Ahmad}/reinvest-preview?amount=1")).StatusCode);
        }
    }

    [Fact]
    public async Task طلبات_متزامنة_بمفاتيح_مختلفة_على_نفس_الرصيد_بينجح_واحد_بس_والرصيد_ما_بيصير_سالب()
    {
        var s = await SetUpAsync(); // رصيد أحمد 2.250
        var gate = new TaskCompletionSource();
        var tasks = Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            await gate.Task;
            return await ReinvestAsync(s.Admin, s.Ahmad, 2.000m, DatabaseFixture.AdminPassword);
        })).ToArray();
        await Task.Delay(100);
        gate.SetResult();
        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, r => Assert.True((int)r.StatusCode < 500, $"{(int)r.StatusCode}"));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.Created),
            r => Assert.True(r.StatusCode is HttpStatusCode.UnprocessableEntity or HttpStatusCode.Conflict));

        var partner = await PartnerAsync(s.Admin, s.Ahmad);
        Assert.Equal(0.250m, partner.GetProperty("currentBalance").GetDecimal());
        Assert.Equal(12m, partner.GetProperty("capitalBalance").GetDecimal());

        using var scope = CreateScope();
        var violations = await Chaos.InvariantAuditor.AuditAsync(CreateDbContext(scope));
        Assert.True(violations.Count == 0, string.Join('\n', violations));
    }

    // ===================================================================== التراجع عن إعادة الاستثمار

    private static Task<HttpResponseMessage> ReverseAsync(HttpClient client, Guid reinvestmentId, string? password, Guid? requestId = null, string? reason = null) =>
        client.PostAsJsonAsync($"/api/v1/partners/reinvestments/{reinvestmentId}/reverse", new { password, reason, clientRequestId = requestId ?? Guid.NewGuid() });

    private async Task<Guid> ReinvestOkAsync(Setup s, decimal amount, string? notes = null)
    {
        var response = await ReadJsonAsync(await ReinvestAsync(s.Admin, s.Ahmad, amount, DatabaseFixture.AdminPassword, notes: notes), "إعادة استثمار");
        return response.GetProperty("reinvestmentId").GetGuid();
    }

    private async Task<JsonElement> ReinvestmentsAsync(HttpClient admin, Guid partnerId) =>
        await ReadJsonAsync(await admin.GetAsync($"/api/v1/partners/{partnerId}/reinvestments"), "قائمة إعادات الاستثمار");

    [Fact]
    public async Task تراجع_كامل_بيرجّع_المبلغ_للرصيد_وبينقصه_من_رأس_المال_والنسبة_بدون_ما_يلمس_الإعادة_الأصلية()
    {
        var s = await SetUpAsync();
        var id = await ReinvestOkAsync(s, 2.000m);

        var list = await ReinvestmentsAsync(s.Admin, s.Ahmad);
        Assert.Single(list.EnumerateArray());
        Assert.False(list[0].GetProperty("isReversed").GetBoolean());

        // معاينة بلا كتابة: الرصيد 0.250 ← 2.250، رأس المال 12 ← 10، النسبة 54.55 ← 50.
        var preview = await ReadJsonAsync(await s.Admin.GetAsync($"/api/v1/partners/reinvestments/{id}/reverse-preview"), "معاينة التراجع");
        Assert.True(preview.GetProperty("canReverse").GetBoolean());
        Assert.Equal(0.250m, preview.GetProperty("balanceBefore").GetDecimal());
        Assert.Equal(2.250m, preview.GetProperty("balanceAfter").GetDecimal());
        Assert.Equal(12m, preview.GetProperty("capitalBefore").GetDecimal());
        Assert.Equal(10m, preview.GetProperty("capitalAfter").GetDecimal());
        Assert.Equal(54.55m, preview.GetProperty("sharePercentBefore").GetDecimal());
        Assert.Equal(50.00m, preview.GetProperty("sharePercentAfter").GetDecimal());
        Assert.Equal(0.250m, (await PartnerAsync(s.Admin, s.Ahmad)).GetProperty("currentBalance").GetDecimal()); // المعاينة ما غيّرت شي

        int drawerLogsBefore;
        using (var scope = CreateScope())
        {
            drawerLogsBefore = await CreateDbContext(scope).CashDrawerLogs.IgnoreQueryFilters().CountAsync();
        }

        var response = await ReadJsonAsync(await ReverseAsync(s.Admin, id, DatabaseFixture.AdminPassword, reason: "قرار تغيّر"), "تراجع");
        Assert.False(response.GetProperty("wasReplay").GetBoolean());
        Assert.Equal(2.250m, response.GetProperty("newBalance").GetDecimal());
        Assert.Equal(12m, response.GetProperty("capitalBefore").GetDecimal());
        Assert.Equal(10m, response.GetProperty("capitalAfter").GetDecimal());

        var partner = await PartnerAsync(s.Admin, s.Ahmad);
        Assert.Equal(2.250m, partner.GetProperty("currentBalance").GetDecimal());
        Assert.Equal(10m, partner.GetProperty("capitalBalance").GetDecimal());
        Assert.Equal(0m, partner.GetProperty("reinvestedTotal").GetDecimal());

        var after = await ReinvestmentsAsync(s.Admin, s.Ahmad);
        Assert.True(after[0].GetProperty("isReversed").GetBoolean());
        Assert.Equal("قرار تغيّر", after[0].GetProperty("reversalReason").GetString());
        Assert.False(string.IsNullOrWhiteSpace(after[0].GetProperty("reversedByName").GetString()));

        var ledger = await ReadJsonAsync(await s.Admin.GetAsync($"/api/v1/partners/{s.Ahmad}/ledger"), "كشف حساب");
        var kinds = ledger.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("kindCode").GetString()).ToList();
        Assert.Contains("reinvest", kinds);
        Assert.Contains("reinvest-reversal", kinds);
        Assert.Equal(2.250m, ledger.GetProperty("currentBalance").GetDecimal());

        using (var scope = CreateScope())
        {
            var db = CreateDbContext(scope);
            Assert.Equal(drawerLogsBefore, await db.CashDrawerLogs.IgnoreQueryFilters().CountAsync()); // بلا حركة نقد
            var reinvestment = await db.PartnerReinvestments.IgnoreQueryFilters().AsNoTracking().SingleAsync();
            Assert.Equal(2.000m, reinvestment.Amount); // الأصل ما اتلمس
            var reversal = await db.PartnerReinvestmentReversals.IgnoreQueryFilters().AsNoTracking().SingleAsync();
            var withdrawal = await db.CapitalTransactions.IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.Id == reversal.CapitalTransactionId);
            Assert.Equal(SupermarketSystem.Domain.Finance.CapitalTransactionType.Withdrawal, withdrawal.Type);
            Assert.Equal((2.000m, s.Ahmad), (withdrawal.Amount, withdrawal.PartnerId));
            var violations = await Chaos.InvariantAuditor.AuditAsync(db);
            Assert.True(violations.Count == 0, string.Join('\n', violations));
            var severities = await db.Notifications.IgnoreQueryFilters().AsNoTracking()
                .Where(n => n.Title.Contains("تراجع عن إعادة استثمار")).Select(n => n.Severity).ToListAsync();
            Assert.NotEmpty(severities);
            Assert.All(severities, sev => Assert.Equal(NotificationSeverity.Warning, sev));
        }
    }

    [Fact]
    public async Task التراجع_مرة_وحدة_بس_والإعادة_بنفس_المفتاح_رد_أصلي_بلا_كلمة_سر_والتراجع_التاني_409()
    {
        var s = await SetUpAsync();
        var id = await ReinvestOkAsync(s, 2.000m);
        var requestId = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Created, (await ReverseAsync(s.Admin, id, DatabaseFixture.AdminPassword, requestId)).StatusCode);
        var replay = await ReverseAsync(s.Admin, id, "مش مهم", requestId);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.True(JsonDocument.Parse(await replay.Content.ReadAsStringAsync()).RootElement.GetProperty("wasReplay").GetBoolean());

        var again = await ReverseAsync(s.Admin, id, DatabaseFixture.AdminPassword); // مفتاح جديد = تراجع تاني
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Contains("PartnerReinvest.AlreadyReversed", await again.Content.ReadAsStringAsync());

        var partner = await PartnerAsync(s.Admin, s.Ahmad);
        Assert.Equal(2.250m, partner.GetProperty("currentBalance").GetDecimal());
        Assert.Equal(10m, partner.GetProperty("capitalBalance").GetDecimal());
        using var scope = CreateScope();
        Assert.Equal(1, await CreateDbContext(scope).PartnerReinvestmentReversals.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task تراجع_بكلمة_سر_غلط_أو_فاضية_أو_حساب_تاني_مرفوض_وما_بينكتب_ولا_شي()
    {
        var s = await SetUpAsync();
        var id = await ReinvestOkAsync(s, 2.000m);
        foreach (var bad in new string?[] { "غلط", "", null, UsersTestDataHelper.DefaultPassword })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await ReverseAsync(s.Admin, id, bad)).StatusCode);
        }

        var partner = await PartnerAsync(s.Admin, s.Ahmad);
        Assert.Equal(0.250m, partner.GetProperty("currentBalance").GetDecimal());
        Assert.Equal(12m, partner.GetProperty("capitalBalance").GetDecimal());
        using var scope = CreateScope();
        Assert.Equal(0, await CreateDbContext(scope).PartnerReinvestmentReversals.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task التراجع_مرفوض_لو_رأس_المال_انسحب_بعد_الإعادة_وبيفتح_لما_يكفي_المبلغ()
    {
        var s = await SetUpAsync();
        var id = await ReinvestOkAsync(s, 2.000m); // رأس المال 12

        async Task WithdrawCapitalAsync(decimal amount) => await ReadJsonAsync(await s.Admin.PostAsJsonAsync("/api/v1/finance/capital-transactions", new
        {
            branchId = Fixture.TestBranchId, type = "Withdrawal", amount, occurredAtUtc = DateTime.UtcNow, notes = "سحب", partnerId = s.Ahmad
        }, StringEnums), "سحب رأس مال");

        await WithdrawCapitalAsync(11m); // رأس المال 1 < 2
        var blocked = await ReverseAsync(s.Admin, id, DatabaseFixture.AdminPassword);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, blocked.StatusCode);
        Assert.Contains("PartnerReinvest.CapitalInsufficient", await blocked.Content.ReadAsStringAsync());
        var preview = await ReadJsonAsync(await s.Admin.GetAsync($"/api/v1/partners/reinvestments/{id}/reverse-preview"), "معاينة");
        Assert.False(preview.GetProperty("canReverse").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(preview.GetProperty("blockedReason").GetString()));
        Assert.Equal(0.250m, (await PartnerAsync(s.Admin, s.Ahmad)).GetProperty("currentBalance").GetDecimal());

        await ReadJsonAsync(await s.Admin.PostAsJsonAsync("/api/v1/finance/capital-transactions", new
        {
            branchId = Fixture.TestBranchId, type = "Deposit", amount = 1m, occurredAtUtc = DateTime.UtcNow, notes = "إيداع", partnerId = s.Ahmad
        }, StringEnums), "إيداع"); // رأس المال 2 = كافي بالضبط
        Assert.Equal(HttpStatusCode.Created, (await ReverseAsync(s.Admin, id, DatabaseFixture.AdminPassword)).StatusCode);
        var partner = await PartnerAsync(s.Admin, s.Ahmad);
        Assert.Equal(2.250m, partner.GetProperty("currentBalance").GetDecimal());
        Assert.Equal(0m, partner.GetProperty("capitalBalance").GetDecimal());
    }

    [Fact]
    public async Task تراجع_عن_إعادة_وحدة_من_اثنتين_والدورة_إعادة_ثم_تراجع_ثم_إعادة_والشريك_الموقوف_بيتراجع_عنه()
    {
        var s = await SetUpAsync();
        var first = await ReinvestOkAsync(s, 1.000m);
        var second = await ReinvestOkAsync(s, 0.500m);
        Assert.Equal(HttpStatusCode.Created, (await ReverseAsync(s.Admin, first, DatabaseFixture.AdminPassword)).StatusCode);

        var partner = await PartnerAsync(s.Admin, s.Ahmad);
        Assert.Equal(1.750m, partner.GetProperty("currentBalance").GetDecimal()); // 2.250 − 0.500
        Assert.Equal(10.500m, partner.GetProperty("capitalBalance").GetDecimal());
        Assert.Equal(0.500m, partner.GetProperty("reinvestedTotal").GetDecimal());
        var list = (await ReinvestmentsAsync(s.Admin, s.Ahmad)).EnumerateArray().ToDictionary(e => e.GetProperty("id").GetGuid());
        Assert.True(list[first].GetProperty("isReversed").GetBoolean());
        Assert.False(list[second].GetProperty("isReversed").GetBoolean());

        // بعد التراجع بيقدر يعيد استثمار نفس المبلغ من جديد.
        await ReadJsonAsync(await ReinvestAsync(s.Admin, s.Ahmad, 1.750m, DatabaseFixture.AdminPassword), "إعادة جديدة");
        Assert.Equal(0m, (await PartnerAsync(s.Admin, s.Ahmad)).GetProperty("currentBalance").GetDecimal());

        // الشريك الموقوف: التراجع بيشتغل (بيرجّع رصيد/رأس مال بس).
        await ReadJsonAsync(await s.Admin.PostAsJsonAsync($"/api/v1/partners/{s.Ahmad}/active", new { isActive = false }), "إيقاف");
        Assert.Equal(HttpStatusCode.Created, (await ReverseAsync(s.Admin, second, DatabaseFixture.AdminPassword)).StatusCode);
        Assert.Equal(0.500m, (await PartnerAsync(s.Admin, s.Ahmad)).GetProperty("currentBalance").GetDecimal());
    }

    [Fact]
    public async Task التراجع_والقائمة_والمعاينة_ممنوعة_لمساعد_الأدمن_والكاشير_وغير_الموجود_404()
    {
        var s = await SetUpAsync();
        var id = await ReinvestOkAsync(s, 1.000m);
        foreach (var (roleId, app) in new[] { (UsersTestDataHelper.AssistantAdminRoleId, "Admin"), (UsersTestDataHelper.CashierRoleId, "Cashier") })
        {
            var (_, name) = await UsersTestDataHelper.CreateUserWithRoleReturningUsernameAsync(Fixture.Factory.Services, Fixture.TestBranchId, roleId, "reverse.denied");
            var client = await LoginHelper.LoginAsAsync(Fixture, name, UsersTestDataHelper.DefaultPassword, app);
            Assert.Equal(HttpStatusCode.Forbidden, (await ReverseAsync(client, id, UsersTestDataHelper.DefaultPassword)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/v1/partners/reinvestments/{id}/reverse-preview")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/v1/partners/{s.Ahmad}/reinvestments")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await ReverseAsync(s.Admin, Guid.NewGuid(), DatabaseFixture.AdminPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Admin.GetAsync($"/api/v1/partners/reinvestments/{Guid.NewGuid()}/reverse-preview")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Admin.GetAsync($"/api/v1/partners/{Guid.NewGuid()}/reinvestments")).StatusCode);
    }

    [Fact]
    public async Task تراجعات_متزامنة_لنفس_الإعادة_بمفاتيح_مختلفة_بينجح_واحد_بس_والرصيد_ورأس_المال_ما_بيتضاعفوا()
    {
        var s = await SetUpAsync();
        var id = await ReinvestOkAsync(s, 2.000m);
        var gate = new TaskCompletionSource();
        var tasks = Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            await gate.Task;
            return await ReverseAsync(s.Admin, id, DatabaseFixture.AdminPassword);
        })).ToArray();
        await Task.Delay(100);
        gate.SetResult();
        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, r => Assert.True((int)r.StatusCode < 500, $"{(int)r.StatusCode}"));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));

        var partner = await PartnerAsync(s.Admin, s.Ahmad);
        Assert.Equal(2.250m, partner.GetProperty("currentBalance").GetDecimal());
        Assert.Equal(10m, partner.GetProperty("capitalBalance").GetDecimal());
        using var scope = CreateScope();
        var violations = await Chaos.InvariantAuditor.AuditAsync(CreateDbContext(scope));
        Assert.True(violations.Count == 0, string.Join('\n', violations));
    }
}
