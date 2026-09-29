using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SupermarketSystem.Domain.Finance;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Finance;

/// <summary>
/// المصاريف والرواتب (29/9/2026، "الثلاثة اعملهم، وخليني انا اعرف نوع المصاريف") عبر HTTP حقيقي بأرقام محسوبة باليد:
///   1. أنواع مصاريف بيعرّفها صاحب المحل (فريدة، بتنوقف بدل الحذف، "رواتب"/"أخرى" ما بينوقفوا).
///   2. مصروف "من الصندوق" بينقص المتوقع بالتقفيل (بلا عجز وهمي)، و"من برّا" ما بيأثر.
///   3. موظفين: سلف (مش مصروف) + راتب (مصروف رواتب بكامله) بخصم السلفة، والصندوق بينقص باللي طلع فعليًا.
/// أسماء الأنواع الجديدة فريدة (Guid) لأن جدول الأنواع ما بيتصفّر بين الاختبارات (بذور HasData).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ExpensesAndEmployeesHttpTests : IntegrationTestBase
{
    public ExpensesAndEmployeesHttpTests(DatabaseFixture fixture) : base(fixture) { }

    private static async Task<JsonElement> OkJsonAsync(HttpResponseMessage response, string what)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{what}: {(int)response.StatusCode} {body}");
        return string.IsNullOrWhiteSpace(body) ? default : JsonDocument.Parse(body).RootElement;
    }

    private static async Task AssertStatusAsync(HttpResponseMessage response, HttpStatusCode expected, string what)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"{what}: متوقع {(int)expected} وطلع {(int)response.StatusCode} {body}");
    }

    /// <summary>بيعة كاش بسعر معيّن (كاش بالدرج عشان التقفيل).</summary>
    private async Task SellCashAsync(HttpClient admin, decimal price, decimal quantity)
    {
        Guid productId, unitId;
        using (var scope = CreateScope())
        {
            var db = CreateDbContext(scope);
            await TestDataBuilder.EnsureDocumentSequencesProvisionedAsync(db, Fixture.TestBranchId);
            var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, $"منتج مصاريف {Guid.NewGuid():N}");
            await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, price);
            await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 100m);
            productId = product.Id;
            unitId = unit.Id;
        }

        await OkJsonAsync(await admin.PostAsJsonAsync("/api/v1/sales", new
        {
            branchId = Fixture.TestBranchId, clientRequestId = Guid.NewGuid(), customerId = (Guid?)null, invoiceLevelDiscountAmount = 0m,
            items = new[] { new { productId, productUnitId = unitId, quantity, manualDiscountAmount = 0m, productBatchId = (Guid?)null } },
            payments = new[] { new { paymentMethodId = TestDataBuilder.CashPaymentMethodId, amount = price * quantity, externalReference = (string?)null, clientRequestId = Guid.NewGuid() } }
        }), "بيع");
    }

    private async Task<JsonElement> CloseAsync(HttpClient admin, decimal counted) =>
        await OkJsonAsync(await admin.PostAsJsonAsync("/api/v1/cash-closings", new
        {
            branchId = Fixture.TestBranchId, businessDate = DateOnly.FromDateTime(DateTime.UtcNow), countedCash = counted,
            countedDetails = Array.Empty<object>()
        }), "تقفيل");

    [Fact]
    public async Task أنواع_مصاريف_بيعرّفها_صاحب_المحل_والمصروف_من_الصندوق_بينقص_المتوقع_بالتقفيل()
    {
        var admin = await CreateAuthenticatedClientAsync();
        var now = DateTime.UtcNow;

        // الأنواع الجاهزة
        var seeded = (await OkJsonAsync(await admin.GetAsync("/api/v1/finance/expense-types?includeInactive=true"), "الأنواع")).EnumerateArray().ToList();
        foreach (var name in new[] { "إيجار", "كهرباء", "ماء", "رواتب", "تنظيف", "أخرى" })
        {
            Assert.Contains(seeded, t => t.GetProperty("name").GetString() == name && t.GetProperty("isBuiltIn").GetBoolean());
        }

        // نوع جديد + مكرر + فاضي
        var maintenanceName = $"صيانة {Guid.NewGuid():N}"[..20];
        var maintenance = await OkJsonAsync(await admin.PostAsJsonAsync("/api/v1/finance/expense-types", new { name = maintenanceName }), "نوع جديد");
        var maintenanceId = maintenance.GetProperty("id").GetGuid();
        Assert.False(maintenance.GetProperty("isBuiltIn").GetBoolean());
        await AssertStatusAsync(await admin.PostAsJsonAsync("/api/v1/finance/expense-types", new { name = maintenanceName }), HttpStatusCode.Conflict, "اسم مكرر");
        await AssertStatusAsync(await admin.PostAsJsonAsync("/api/v1/finance/expense-types", new { name = "  " }), HttpStatusCode.BadRequest, "اسم فاضي");

        await SellCashAsync(admin, 20.000m, 1m);

        // صيانة 3.500 من الصندوق، تنظيف 2.000 من برّا، إيجار 100 بالطريقة القديمة (category بس)
        await OkJsonAsync(await admin.PostAsJsonAsync("/api/v1/finance/expenses", new
        {
            branchId = Fixture.TestBranchId, expenseTypeId = maintenanceId, amount = 3.500m, paymentDateUtc = now,
            periodYear = now.Year, periodMonth = now.Month, notes = "تصليح الثلاجة", paidFromDrawer = true
        }), "مصروف من الصندوق");
        await OkJsonAsync(await admin.PostAsJsonAsync("/api/v1/finance/expenses", new
        {
            branchId = Fixture.TestBranchId, expenseTypeId = ExpenseType.CleaningId, amount = 2.000m, paymentDateUtc = now,
            periodYear = now.Year, periodMonth = now.Month, notes = (string?)null, paidFromDrawer = false
        }), "مصروف من برّا");
        await OkJsonAsync(await admin.PostAsJsonAsync("/api/v1/finance/expenses", new
        {
            branchId = Fixture.TestBranchId, category = 1, amount = 100m, paymentDateUtc = now,
            periodYear = now.Year, periodMonth = now.Month, notes = (string?)null
        }), "مصروف بالطريقة القديمة");
        await AssertStatusAsync(await admin.PostAsJsonAsync("/api/v1/finance/expenses", new
        {
            branchId = Fixture.TestBranchId, amount = 1m, paymentDateUtc = now, periodYear = now.Year, periodMonth = now.Month
        }), HttpStatusCode.BadRequest, "بلا نوع");

        // القائمة: النوع والصندوق
        var list = (await OkJsonAsync(await admin.GetAsync(
            $"/api/v1/finance/expenses?branchId={Fixture.TestBranchId}&periodYear={now.Year}&periodMonth={now.Month}"), "القائمة"))
            .GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(3, list.Count);
        var maintenanceRow = list.Single(e => e.GetProperty("expenseTypeId").GetGuid() == maintenanceId);
        Assert.Equal(maintenanceName, maintenanceRow.GetProperty("expenseTypeName").GetString());
        Assert.True(maintenanceRow.GetProperty("paidFromDrawer").GetBoolean());
        Assert.Equal("إيجار", list.Single(e => e.GetProperty("amount").GetDecimal() == 100m).GetProperty("expenseTypeName").GetString());
        var byType = (await OkJsonAsync(await admin.GetAsync(
            $"/api/v1/finance/expenses?branchId={Fixture.TestBranchId}&expenseTypeId={ExpenseType.CleaningId}"), "فلتر النوع"))
            .GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2.000m, Assert.Single(byType).GetProperty("amount").GetDecimal());

        // كشف الربح حسب النوع
        var statement = await OkJsonAsync(await admin.GetAsync(
            $"/api/v1/finance/profit-statement?branchId={Fixture.TestBranchId}&year={now.Year}&month={now.Month}"), "كشف الربح");
        Assert.Equal(105.500m, statement.GetProperty("totalExpenses").GetDecimal());
        var types = statement.GetProperty("expensesByType").EnumerateArray()
            .ToDictionary(t => t.GetProperty("typeName").GetString()!, t => t.GetProperty("amount").GetDecimal());
        Assert.Equal(3.500m, types[maintenanceName]);
        Assert.Equal(2.000m, types["تنظيف"]);
        Assert.Equal(100m, types["إيجار"]);

        // التقفيل: 20.000 بيع − 3.500 صيانة من الصندوق = 16.500 (التنظيف والإيجار من برّا ما بيأثروا)
        var closing = await CloseAsync(admin, 16.500m);
        Assert.Equal(16.500m, closing.GetProperty("expectedCash").GetDecimal());
        Assert.Equal(0m, closing.GetProperty("variance").GetDecimal());

        // إيقاف النوع: بيختفي من قائمة التسجيل، بيضل بالكل، ومصروف جديد عليه مرفوض
        await OkJsonAsync(await admin.PutAsJsonAsync($"/api/v1/finance/expense-types/{maintenanceId}", new { name = maintenanceName, isActive = false }), "إيقاف");
        var active = (await OkJsonAsync(await admin.GetAsync("/api/v1/finance/expense-types"), "الفعّالة")).EnumerateArray();
        Assert.DoesNotContain(active, t => t.GetProperty("id").GetGuid() == maintenanceId);
        var all = (await OkJsonAsync(await admin.GetAsync("/api/v1/finance/expense-types?includeInactive=true"), "الكل")).EnumerateArray();
        Assert.Equal(1, all.Single(t => t.GetProperty("id").GetGuid() == maintenanceId).GetProperty("expenseCount").GetInt32());
        await AssertStatusAsync(await admin.PostAsJsonAsync("/api/v1/finance/expenses", new
        {
            branchId = Fixture.TestBranchId, expenseTypeId = maintenanceId, amount = 1m, paymentDateUtc = now,
            periodYear = now.Year, periodMonth = now.Month
        }), HttpStatusCode.UnprocessableEntity, "نوع موقوف");

        // "رواتب" ما بينوقف (الرواتب بتنسجّل عليه تلقائيًا)
        await AssertStatusAsync(await admin.PutAsJsonAsync($"/api/v1/finance/expense-types/{ExpenseType.SalaryId}", new { name = "رواتب", isActive = false }),
            HttpStatusCode.UnprocessableEntity, "إيقاف رواتب");
    }

    [Fact]
    public async Task راتب_بخصم_سلفة_مصروف_بكامله_والصندوق_بينقص_باللي_طلع_فعليًا()
    {
        var admin = await CreateAuthenticatedClientAsync();
        var now = DateTime.UtcNow;

        var created = await OkJsonAsync(await admin.PostAsJsonAsync("/api/v1/employees", new
        {
            branchId = Fixture.TestBranchId, fullName = "خالد الموظف", phone = "0791112223", monthlySalary = 300m, notes = (string?)null
        }), "موظف جديد");
        var employeeId = created.GetProperty("employeeId").GetGuid();

        await SellCashAsync(admin, 50.000m, 10m); // 500.000 كاش بالدرج

        async Task<JsonElement> PayAsync(object body, string what) =>
            await OkJsonAsync(await admin.PostAsJsonAsync($"/api/v1/employees/{employeeId}/payments", body), what);

        // سلفة 50 من الصندوق، و20 من برّا
        await PayAsync(new { type = "Advance", amount = 50m, advanceDeducted = 0m, paidFromDrawer = true, clientRequestId = Guid.NewGuid() }, "سلفة من الصندوق");
        var advance2 = await PayAsync(new { type = 2, amount = 20m, advanceDeducted = 0m, paidFromDrawer = false, clientRequestId = Guid.NewGuid() }, "سلفة من برّا");
        Assert.Equal(70m, advance2.GetProperty("outstandingAdvance").GetDecimal());
        Assert.Equal(JsonValueKind.Null, advance2.GetProperty("expenseId").ValueKind);

        // خصم أكتر من السلف مرفوض، وراتب بلا شهر مرفوض
        await AssertStatusAsync(await admin.PostAsJsonAsync($"/api/v1/employees/{employeeId}/payments", new
        {
            type = "Salary", amount = 300m, advanceDeducted = 80m, periodYear = now.Year, periodMonth = now.Month, paidFromDrawer = true, clientRequestId = Guid.NewGuid()
        }), HttpStatusCode.UnprocessableEntity, "خصم أكتر من السلف");
        await AssertStatusAsync(await admin.PostAsJsonAsync($"/api/v1/employees/{employeeId}/payments", new
        {
            type = "Salary", amount = 300m, advanceDeducted = 0m, paidFromDrawer = true, clientRequestId = Guid.NewGuid()
        }), HttpStatusCode.BadRequest, "راتب بلا شهر");

        // الراتب: 300 بخصم 70 من الصندوق → طلع 230
        var salaryRequestId = Guid.NewGuid();
        var salaryBody = new
        {
            type = "Salary", amount = 300m, advanceDeducted = 70m, periodYear = now.Year, periodMonth = now.Month,
            paidFromDrawer = true, notes = "راتب الشهر", clientRequestId = salaryRequestId
        };
        var salary = await PayAsync(salaryBody, "راتب");
        Assert.Equal(230m, salary.GetProperty("netPaid").GetDecimal());
        Assert.Equal(0m, salary.GetProperty("outstandingAdvance").GetDecimal());
        Assert.Equal(0m, salary.GetProperty("paidBeforeForPeriod").GetDecimal());
        Assert.False(salary.GetProperty("wasReplay").GetBoolean());

        // إعادة نفس الطلب = نفس النتيجة بلا تكرار (§3.2)
        var replay = await OkJsonAsync(await admin.PostAsJsonAsync($"/api/v1/employees/{employeeId}/payments", salaryBody), "إعادة");
        Assert.True(replay.GetProperty("wasReplay").GetBoolean());
        Assert.Equal(salary.GetProperty("paymentId").GetGuid(), replay.GetProperty("paymentId").GetGuid());

        // مكافأة 10 لنفس الشهر من برّا: مسموحة، والرد بيحكي إنه انصرف 300 قبل (الواجهة بتنبّه)
        var bonus = await PayAsync(new
        {
            type = "Salary", amount = 10m, advanceDeducted = 0m, periodYear = now.Year, periodMonth = now.Month, paidFromDrawer = false, clientRequestId = Guid.NewGuid()
        }, "مكافأة");
        Assert.Equal(300m, bonus.GetProperty("paidBeforeForPeriod").GetDecimal());

        // قائمة الموظفين
        var employee = (await OkJsonAsync(await admin.GetAsync($"/api/v1/employees?branchId={Fixture.TestBranchId}"), "الموظفين"))
            .EnumerateArray().Single(e => e.GetProperty("id").GetGuid() == employeeId);
        Assert.Equal(0m, employee.GetProperty("outstandingAdvance").GetDecimal());
        Assert.Equal(310m, employee.GetProperty("totalSalariesPaid").GetDecimal());
        Assert.Equal(70m, employee.GetProperty("totalAdvancesGiven").GetDecimal());
        Assert.Equal(now.Month, employee.GetProperty("lastSalaryMonth").GetInt32());

        // سجل الصرف: 4 (السلفتين، الراتب، المكافأة) - الإعادة ما انحسبت
        var payments = (await OkJsonAsync(await admin.GetAsync($"/api/v1/employees/payments?employeeId={employeeId}"), "السجل")).EnumerateArray().ToList();
        Assert.Equal(4, payments.Count);
        Assert.Equal(2, payments.Count(p => p.GetProperty("typeTitle").GetString() == "سلفة"));

        // كشف الربح: الرواتب = 310 (الراتب بكامله + المكافأة)، السلف مش مصروف
        var statement = await OkJsonAsync(await admin.GetAsync(
            $"/api/v1/finance/profit-statement?branchId={Fixture.TestBranchId}&year={now.Year}&month={now.Month}"), "كشف الربح");
        Assert.Equal(310m, statement.GetProperty("totalExpenses").GetDecimal());
        var salaryType = Assert.Single(statement.GetProperty("expensesByType").EnumerateArray());
        Assert.Equal("رواتب", salaryType.GetProperty("typeName").GetString());
        Assert.Equal(310m, salaryType.GetProperty("amount").GetDecimal());

        // قائمة المصاريف: اسم الموظف على مصروف الراتب
        var expenses = (await OkJsonAsync(await admin.GetAsync(
            $"/api/v1/finance/expenses?branchId={Fixture.TestBranchId}&periodYear={now.Year}&periodMonth={now.Month}"), "المصاريف"))
            .GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, expenses.Count);
        Assert.All(expenses, e => Assert.Equal("خالد الموظف", e.GetProperty("employeeName").GetString()));

        // التقفيل: 500 − 50 (سلفة من الصندوق) − 230 (صافي الراتب) = 220
        var closing = await CloseAsync(admin, 220m);
        Assert.Equal(220m, closing.GetProperty("expectedCash").GetDecimal());
        Assert.Equal(0m, closing.GetProperty("variance").GetDecimal());

        // موظف موقوف: ما في سلفة
        await OkJsonAsync(await admin.PutAsJsonAsync($"/api/v1/employees/{employeeId}", new
        {
            fullName = "خالد الموظف", phone = "0791112223", monthlySalary = 300m, notes = "ترك الشغل", isActive = false
        }), "إيقاف موظف");
        await AssertStatusAsync(await admin.PostAsJsonAsync($"/api/v1/employees/{employeeId}/payments", new
        {
            type = "Advance", amount = 5m, advanceDeducted = 0m, paidFromDrawer = true, clientRequestId = Guid.NewGuid()
        }), HttpStatusCode.UnprocessableEntity, "سلفة لموظف موقوف");
        var activeOnly = (await OkJsonAsync(await admin.GetAsync($"/api/v1/employees?branchId={Fixture.TestBranchId}"), "الفعّالين")).EnumerateArray();
        Assert.DoesNotContain(activeOnly, e => e.GetProperty("id").GetGuid() == employeeId);
    }

    [Fact]
    public async Task الموظفين_والأنواع_للإدارة_بس_الكاشير_ممنوع()
    {
        var (_, cashierName) = await UsersTestDataHelper.CreateUserWithRoleReturningUsernameAsync(
            Fixture.Factory.Services, Fixture.TestBranchId, UsersTestDataHelper.CashierRoleId, "emp.cashier");
        var cashier = await LoginHelper.LoginAsAsync(Fixture, cashierName, UsersTestDataHelper.DefaultPassword, appType: "Cashier");

        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("/api/v1/employees")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("/api/v1/finance/expense-types")).StatusCode);
    }
}
