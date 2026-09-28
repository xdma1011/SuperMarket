using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Domain.CashManagement;
using SupermarketSystem.Infrastructure.Services;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Partners;

/// <summary>
/// وحدة الشركاء (28/9/2026) عبر HTTP حقيقي، بأرقام محسوبة مسبقًا:
///
///   الشهر الماضي: بيع 10 حليب × 1.250 = 12.500، تكلفة 10 × 0.800 = 8.000، مصروف 0.500 → صافي ربح 4.000.
///   مضارب (20%) = 0.800 · الباقي 3.200 بين شركاء رأس المال: أحمد (3000) 2.400، سامي (1000) 0.800.
///   بعدين: أحمد بيسحب 1.000 من الصندوق، وبياخد حليب بسعر التكلفة "اخصمها مني" (0.800) → رصيده 0.600.
///   سامي بيسحب 2.000 "من جيب" صاحب المحل → رصيده -1.200 (سلفة)، والمستحق لصاحب المحل 2.000.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PartnersHttpTests : IntegrationTestBase
{
    public PartnersHttpTests(DatabaseFixture fixture) : base(fixture) { }

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

    private async Task<HttpClient> LoginAsync(string username, string password, int appType = 1)
    {
        var client = Fixture.Factory.CreateClient();
        var login = await ReadJsonAsync(await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            username, password, appType, branchId = (Guid?)null, ipAddress = (string?)null, deviceInfo = "TEST"
        }), $"دخول {username}");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.GetProperty("accessToken").GetString());
        return client;
    }

    private sealed record Setup(HttpClient Admin, Guid MilkId, Guid MilkUnit);

    private async Task<Setup> SetUpCatalogAsync()
    {
        var branchId = Fixture.TestBranchId;
        using (var scope = CreateScope())
        {
            await TestDataBuilder.EnsureDocumentSequencesProvisionedAsync(CreateDbContext(scope), branchId);
        }

        var admin = await CreateAuthenticatedClientAsync();
        var category = await ReadJsonAsync(await admin.PostAsJsonAsync("/api/v1/product-categories",
            new { name = "ألبان", parentCategoryId = (Guid?)null }), "تصنيف");
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
        var milkUnit = (await ReadJsonAsync(await admin.GetAsync($"/api/v1/products/{milkId}/units"), "وحدات"))
            .EnumerateArray().Single().GetProperty("id").GetGuid();

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

        return new Setup(admin, milkId, milkUnit);
    }

    private static DateTime PreviousMonthMiddle()
    {
        var now = DateTime.UtcNow;
        return new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-1).AddDays(14);
    }

    private async Task<Guid> CreatePartnerAsync(HttpClient admin, string name, string type, Guid? userId = null, decimal? percent = null)
    {
        var created = await ReadJsonAsync(await admin.PostAsJsonAsync("/api/v1/partners", new
        {
            branchId = Fixture.TestBranchId, fullName = name, type, userId, speculativeProfitPercent = percent, notes = (string?)null
        }, StringEnums), $"شريك {name}");
        return created.GetProperty("partnerId").GetGuid();
    }

    private async Task DepositCapitalAsync(HttpClient admin, Guid partnerId, decimal amount, DateTime at) =>
        await ReadJsonAsync(await admin.PostAsJsonAsync("/api/v1/finance/capital-transactions", new
        {
            branchId = Fixture.TestBranchId, type = "Deposit", amount, occurredAtUtc = at, notes = "رأس مال", partnerId
        }, StringEnums), "رأس مال");

    private async Task<Dictionary<Guid, JsonElement>> PartnersAsync(HttpClient admin) =>
        (await ReadJsonAsync(await admin.GetAsync($"/api/v1/partners?branchId={Fixture.TestBranchId}"), "الشركاء"))
        .EnumerateArray().ToDictionary(p => p.GetProperty("id").GetGuid());

    [Fact]
    public async Task الكشف_الشهري_والسحوبات_والمستحق_لصاحب_المحل_والسحب_من_الكاشير()
    {
        var s = await SetUpCatalogAsync();
        var admin = s.Admin;
        var branchId = Fixture.TestBranchId;
        var previous = PreviousMonthMiddle();

        // === الشهر الماضي: بيع 10 حليب + مصروف 0.500 ===
        await ReadJsonAsync(await admin.PostAsJsonAsync("/api/v1/sales", new
        {
            branchId, clientRequestId = Guid.NewGuid(), customerId = (Guid?)null, invoiceLevelDiscountAmount = 0m,
            items = new[] { new { productId = s.MilkId, productUnitId = s.MilkUnit, quantity = 10m, manualDiscountAmount = 0m, productBatchId = (Guid?)null } },
            payments = new[] { new { paymentMethodId = TestDataBuilder.CashPaymentMethodId, amount = 12.500m, externalReference = (string?)null, clientRequestId = Guid.NewGuid() } }
        }), "بيع");
        using (var scope = CreateScope())
        {
            var db = CreateDbContext(scope);
            await db.SaleInvoices.IgnoreQueryFilters().ExecuteUpdateAsync(x => x.SetProperty(i => i.CreatedAtUtc, previous));
        }

        await ReadJsonAsync(await admin.PostAsJsonAsync("/api/v1/finance/expenses", new
        {
            branchId, category = 2, amount = 0.500m, paymentDateUtc = previous.ToString("yyyy-MM-dd"),
            periodYear = previous.Year, periodMonth = previous.Month, notes = "كهربا"
        }), "مصروف");

        // === الشركاء: أحمد (حساب الأدمن) وسامي رأس مال، ومضارب 20% ===
        var (samiUserId, samiUsername) = await UsersTestDataHelper.CreateUserWithRoleReturningUsernameAsync(
            Fixture.Factory.Services, branchId, UsersTestDataHelper.CashierRoleId, "partner.sami");
        var ahmad = await CreatePartnerAsync(admin, "أحمد", "Capital", Fixture.AdminUserId);
        var sami = await CreatePartnerAsync(admin, "سامي", "Capital", samiUserId);
        var mudarib = await CreatePartnerAsync(admin, "خالد المضارب", "Speculative", percent: 20m);

        // مجموع نسب المضاربين ما بيتجاوز 100%، والمضارب ما إله رأس مال.
        var tooMuch = await admin.PostAsJsonAsync("/api/v1/partners", new
        {
            branchId, fullName = "مضارب تاني", type = "Speculative", userId = (Guid?)null, speculativeProfitPercent = 85m, notes = (string?)null
        }, StringEnums);
        Assert.Equal(HttpStatusCode.BadRequest, tooMuch.StatusCode);
        var capitalForMudarib = await admin.PostAsJsonAsync("/api/v1/finance/capital-transactions", new
        {
            branchId, type = "Deposit", amount = 100m, occurredAtUtc = previous, notes = (string?)null, partnerId = mudarib
        }, StringEnums);
        Assert.Equal(HttpStatusCode.BadRequest, capitalForMudarib.StatusCode);

        await DepositCapitalAsync(admin, ahmad, 3000m, previous.AddDays(-5));
        await DepositCapitalAsync(admin, sami, 1000m, previous.AddDays(-5));
        // رأس مال انضاف بعد نهاية الشهر ما بيدخل بنسبة هالشهر.
        await DepositCapitalAsync(admin, sami, 5000m, DateTime.UtcNow);

        // === الكشف ===
        var statement = await ReadJsonAsync(await admin.PostAsJsonAsync("/api/v1/partners/statements",
            new { branchId, year = previous.Year, month = previous.Month }), "إصدار كشف");
        Assert.Equal(4.000m, statement.GetProperty("netProfit").GetDecimal());
        Assert.Equal(0m, statement.GetProperty("unallocatedAmount").GetDecimal());
        var lines = statement.GetProperty("lines").EnumerateArray().ToDictionary(l => l.GetProperty("partnerId").GetGuid());
        Assert.Equal(0.800m, lines[mudarib].GetProperty("shareAmount").GetDecimal());
        Assert.Equal(2.400m, lines[ahmad].GetProperty("shareAmount").GetDecimal());
        Assert.Equal(0.800m, lines[sami].GetProperty("shareAmount").GetDecimal());
        Assert.Equal(3000m, lines[ahmad].GetProperty("capitalBalance").GetDecimal());

        // إعادة الإصدار ما بتضاعف الأنصبة.
        await ReadJsonAsync(await admin.PostAsJsonAsync("/api/v1/partners/statements",
            new { branchId, year = previous.Year, month = previous.Month }), "إعادة إصدار");
        var partners = await PartnersAsync(admin);
        Assert.Equal(2.400m, partners[ahmad].GetProperty("currentBalance").GetDecimal());

        // الشهر الحالي لسه ما خلص.
        var now = DateTime.UtcNow;
        var current = await admin.PostAsJsonAsync("/api/v1/partners/statements", new { branchId, year = now.Year, month = now.Month });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, current.StatusCode);

        // === سحوبات ===
        await ReadJsonAsync(await admin.PostAsJsonAsync("/api/v1/partners/withdrawals", new
        {
            partnerId = ahmad, amount = 1.000m, source = "Drawer", notes = "مصروف بيت", occurredAtUtc = (DateTime?)null, clientRequestId = Guid.NewGuid()
        }, StringEnums), "سحب من الصندوق");
        var pocketRequest = Guid.NewGuid();
        var pocket = await ReadJsonAsync(await admin.PostAsJsonAsync("/api/v1/partners/withdrawals", new
        {
            partnerId = sami, amount = 2.000m, source = "OwnerPocket", notes = (string?)null, occurredAtUtc = (DateTime?)null, clientRequestId = pocketRequest
        }, StringEnums), "سحب من الجيب");
        Assert.Equal(-1.200m, pocket.GetProperty("newBalance").GetDecimal());
        // إعادة إرسال نفس الطلب ما بتسحب مرتين.
        var replay = await ReadJsonAsync(await admin.PostAsJsonAsync("/api/v1/partners/withdrawals", new
        {
            partnerId = sami, amount = 2.000m, source = "OwnerPocket", notes = (string?)null, occurredAtUtc = (DateTime?)null, clientRequestId = pocketRequest
        }, StringEnums), "إعادة إرسال");
        Assert.True(replay.GetProperty("wasReplay").GetBoolean());

        // "اخصمها مني" (سحب بسعر التكلفة) بينخصم من رصيد أحمد لأنه حسابه.
        await ReadJsonAsync(await admin.PostAsJsonAsync("/api/v1/sales/at-cost", new
        {
            branchId, clientRequestId = Guid.NewGuid(), deductFromShare = true, paymentMethodId = (Guid?)null,
            items = new[] { new { productId = s.MilkId, productUnitId = s.MilkUnit, quantity = 1m } }
        }), "سحب بسعر التكلفة");

        partners = await PartnersAsync(admin);
        Assert.Equal(0.600m, partners[ahmad].GetProperty("currentBalance").GetDecimal());
        Assert.Equal(0.800m, partners[ahmad].GetProperty("atCostDeductedTotal").GetDecimal());
        Assert.Equal(-1.200m, partners[sami].GetProperty("currentBalance").GetDecimal());
        Assert.Equal(6000m, partners[sami].GetProperty("capitalBalance").GetDecimal());

        var ledger = await ReadJsonAsync(await admin.GetAsync($"/api/v1/partners/{ahmad}/ledger"), "كشف حساب");
        Assert.Equal(0.600m, ledger.GetProperty("currentBalance").GetDecimal());
        Assert.Equal(new[] { "share", "withdrawal", "at-cost" },
            ledger.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("kindCode").GetString()).ToArray());

        // === مستحق لصاحب المحل ===
        var receivables = (await ReadJsonAsync(await admin.GetAsync($"/api/v1/partners/owner-receivables?branchId={branchId}"), "المستحق"))
            .EnumerateArray().Single();
        Assert.Equal(Fixture.AdminUserId, receivables.GetProperty("ownerUserId").GetGuid());
        Assert.Equal(2.000m, receivables.GetProperty("balance").GetDecimal());

        var tooBig = await admin.PostAsJsonAsync("/api/v1/partners/owner-receivables/repayments", new
        {
            branchId, ownerUserId = Fixture.AdminUserId, amount = 5m, source = "Drawer", notes = (string?)null, clientRequestId = Guid.NewGuid()
        }, StringEnums);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooBig.StatusCode);
        var repaid = await ReadJsonAsync(await admin.PostAsJsonAsync("/api/v1/partners/owner-receivables/repayments", new
        {
            branchId, ownerUserId = Fixture.AdminUserId, amount = 0.500m, source = "Drawer", notes = (string?)null, clientRequestId = Guid.NewGuid()
        }, StringEnums), "استرجاع");
        Assert.Equal(1.500m, repaid.GetProperty("remainingBalance").GetDecimal());

        // === السحب من الكاشير بحساب الشريك نفسه ===
        var (_, cashierUsername) = await UsersTestDataHelper.CreateUserWithRoleReturningUsernameAsync(
            Fixture.Factory.Services, branchId, UsersTestDataHelper.CashierRoleId, "cashier.partners");
        var cashier = await LoginAsync(cashierUsername, UsersTestDataHelper.DefaultPassword);

        var wrong = await cashier.PostAsJsonAsync("/api/v1/partner-withdrawals/verified", new
        {
            branchId, username = samiUsername, password = "غلط", amount = 0.250m, notes = (string?)null, clientRequestId = Guid.NewGuid()
        });
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);

        var notPartner = await cashier.PostAsJsonAsync("/api/v1/partner-withdrawals/verified", new
        {
            branchId, username = cashierUsername, password = UsersTestDataHelper.DefaultPassword, amount = 0.250m, notes = (string?)null, clientRequestId = Guid.NewGuid()
        });
        Assert.Equal(HttpStatusCode.Forbidden, notPartner.StatusCode);

        var atCashier = await ReadJsonAsync(await cashier.PostAsJsonAsync("/api/v1/partner-withdrawals/verified", new
        {
            branchId, username = samiUsername, password = UsersTestDataHelper.DefaultPassword, amount = 0.250m, notes = "من الكاشير", clientRequestId = Guid.NewGuid()
        }), "سحب من الكاشير");
        Assert.Equal("سامي", atCashier.GetProperty("partnerName").GetString());
        Assert.Equal(-1.450m, atCashier.GetProperty("newBalance").GetDecimal());

        // الكاشير ما بيشوف صفحة الشركاء.
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("/api/v1/partners")).StatusCode);

        using (var scope = CreateScope())
        {
            var db = CreateDbContext(scope);
            // سحب أحمد (1.000) وسحب الكاشير (0.250) واسترجاع صاحب المحل (0.500) طلعوا من الصندوق؛ سحب الجيب لأ.
            var payOuts = await db.CashDrawerLogs.IgnoreQueryFilters()
                .Where(l => l.BranchId == branchId && l.MovementType == CashDrawerMovementType.PayOut)
                .SumAsync(l => l.Amount);
            Assert.Equal(1.750m, payOuts);

            var withdrawals = await db.PartnerWithdrawals.IgnoreQueryFilters().ToListAsync();
            Assert.Single(withdrawals, w => w.RecordedAtCashier);

            var alerts = await db.Notifications.IgnoreQueryFilters().Where(n => n.Title.StartsWith("سحب شريك")).Select(n => n.Message).Distinct().CountAsync();
            Assert.Equal(3, alerts);
        }
    }

    [Fact]
    public async Task شهر_خسارة_والتوليد_التلقائي_وفرع_بلا_رأس_مال()
    {
        var s = await SetUpCatalogAsync();
        var admin = s.Admin;
        var branchId = Fixture.TestBranchId;
        var previous = PreviousMonthMiddle();

        var ahmad = await CreatePartnerAsync(admin, "أحمد", "Capital");
        var sami = await CreatePartnerAsync(admin, "سامي", "Capital");
        var mudarib = await CreatePartnerAsync(admin, "المضارب", "Speculative", percent: 10m);

        // بلا رأس مال مسجَّل - الكشف بينرفض برسالة واضحة، والتوليد التلقائي بيبعت تنبيه.
        var noCapital = await admin.PostAsJsonAsync("/api/v1/partners/statements", new { branchId, year = previous.Year, month = previous.Month });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, noCapital.StatusCode);
        Assert.Contains("رأس مال", await noCapital.Content.ReadAsStringAsync());

        // الخدمات الخلفية مش شغّالة ببيئة الاختبار - بنبنيها ونشغّل دورة وحدة يدويًا.
        var service = new PartnerStatementBackgroundService(
            Fixture.Factory.Services.GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PartnerStatementBackgroundService>.Instance);
        await service.RunAsync(CancellationToken.None);
        using (var scope = CreateScope())
        {
            var db = CreateDbContext(scope);
            Assert.True(await db.Notifications.IgnoreQueryFilters().AnyAsync(n => n.Title.StartsWith("ما نزل كشف الشركاء")));
            Assert.False(await db.PartnerMonthlyStatements.IgnoreQueryFilters().AnyAsync());
        }

        // شهر خسارة (مصروف بلا مبيعات): المضارب صفر، والخسارة على شركاء رأس المال بنسبة رأس مالهم.
        await ReadJsonAsync(await admin.PostAsJsonAsync("/api/v1/finance/expenses", new
        {
            branchId, category = 2, amount = 1.000m, paymentDateUtc = previous.ToString("yyyy-MM-dd"),
            periodYear = previous.Year, periodMonth = previous.Month, notes = "إيجار"
        }), "مصروف");
        await DepositCapitalAsync(admin, ahmad, 750m, previous.AddDays(-3));
        await DepositCapitalAsync(admin, sami, 250m, previous.AddDays(-3));

        await service.RunAsync(CancellationToken.None);
        using (var scope = CreateScope())
        {
            var db = CreateDbContext(scope);
            var statement = await db.PartnerMonthlyStatements.IgnoreQueryFilters().Include(x => x.Lines).SingleAsync();
            Assert.True(statement.IsAutomatic);
            Assert.Equal(-1.000m, statement.NetProfit);
            var byPartner = statement.Lines.ToDictionary(l => l.PartnerId, l => l.ShareAmount);
            Assert.Equal(0m, byPartner[mudarib]);
            Assert.Equal(-0.750m, byPartner[ahmad]);
            Assert.Equal(-0.250m, byPartner[sami]);
        }

        // التوليد التلقائي مرة تانية ما بيعمل كشف تاني ولا بيغيّره.
        await service.RunAsync(CancellationToken.None);
        using (var scope = CreateScope())
        {
            Assert.Equal(1, await CreateDbContext(scope).PartnerMonthlyStatements.IgnoreQueryFilters().CountAsync());
        }

        var summaries = await ReadJsonAsync(await admin.GetAsync($"/api/v1/partners/statements?branchId={branchId}"), "الكشوف");
        Assert.True(summaries.GetArrayLength() == 1, summaries.GetRawText());
        Assert.True(summaries.EnumerateArray().Single().GetProperty("isAutomatic").GetBoolean());
    }
}
