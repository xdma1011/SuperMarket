using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

// ===================================================================
// TestDataSeeder - بيعبّي قاعدة تست فاضية ببيانات جاهزة للتست الشامل
// (docs/FULL_TEST_PLAYBOOK.md)، عن طريق الـAPI نفسه (نفس الطلبات اللي
// لوحة الإدارة بتبعتها)، مش كتابة مباشرة بالقاعدة - فأي خطأ بالإعداد
// نفسه بينكشف هون زي ما كان رح ينكشف لصاحب المحل.
//
// الاستخدام (الـAPI لازم يكون شغّال):
//   dotnet run --project tools/TestDataSeeder -- [--api http://localhost:5200]
//
// على قاعدة فاضية بيعمل bootstrap-admin أول (admin / 123). لو البيانات
// موجودة أصلًا (تصنيف "بيانات تست")، بيطبع الموجود وبيطلع بلا تكرار.
// للتست بس - ما في داعي تشغّله على قاعدة فيها بيانات حقيقية.
// ===================================================================

var apiBase = "http://localhost:5200";
for (var i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--api") apiBase = args[i + 1].TrimEnd('/');
}

const string MarkerCategory = "بيانات تست";
const string Password = "123";
var today = DateOnly.FromDateTime(DateTime.Now);

using var http = new HttpClient { BaseAddress = new Uri(apiBase) };

async Task<JsonElement> Send(HttpMethod method, string url, object? body = null, bool allowFailure = false)
{
    using var request = new HttpRequestMessage(method, url);
    if (body is not null) request.Content = JsonContent.Create(body);
    using var response = await http.SendAsync(request);
    var text = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode)
    {
        if (allowFailure) return default;
        throw new InvalidOperationException($"{method} {url} -> {(int)response.StatusCode}\n{text}");
    }
    return string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
}

Task<JsonElement> Get(string url) => Send(HttpMethod.Get, url);
Task<JsonElement> Post(string url, object body) => Send(HttpMethod.Post, url, body);

// بعض القوائم بترجع مصفوفة وبعضها صفحة { items: [...] }.
static IEnumerable<JsonElement> Items(JsonElement element) =>
    element.ValueKind == JsonValueKind.Array ? element.EnumerateArray() : element.GetProperty("items").EnumerateArray();

static Guid Id(JsonElement element, string property) => element.GetProperty(property).GetGuid();

void Step(string text) => Console.WriteLine($"  ✓ {text}");

Console.WriteLine($"الـAPI: {apiBase}");
try
{
    await Get("/api/v1/auth/branches");
}
catch (Exception ex)
{
    Console.WriteLine($"✗ الـAPI مش شغّال أو مش على هالعنوان: {ex.Message}");
    return 1;
}

// ---------- 1) أول مستخدم (مرة وحدة على قاعدة فاضية) ----------
var bootstrap = await Send(HttpMethod.Post, "/api/v1/system/bootstrap-admin", new { }, allowFailure: true);
Step(bootstrap.ValueKind == JsonValueKind.Undefined
    ? "في مستخدمين أصلًا - تخطّيت bootstrap-admin"
    : "bootstrap-admin: انعمل admin / 123 والفرع الرئيسي");

// ---------- 2) دخول الأدمن ----------
var login = await Post("/api/v1/auth/login", new
{
    username = "admin", password = Password, appType = "Admin",
    branchId = (Guid?)null, ipAddress = (string?)null, deviceInfo = "TestDataSeeder"
});
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.GetProperty("accessToken").GetString());
Step("دخول admin");

var branches = Items(await Get("/api/v1/branches?pageSize=100")).ToList();
var mainBranch = branches.First();
var mainBranchId = Id(mainBranch, "id");

var categories = Items(await Get("/api/v1/product-categories?pageSize=200")).ToList();
if (categories.Any(c => c.GetProperty("name").GetString() == MarkerCategory))
{
    Console.WriteLine($"\nالبيانات موجودة أصلًا (تصنيف \"{MarkerCategory}\") - ما عملت إشي جديد.");
    Console.WriteLine("لبيانات جديدة: قاعدة تست فاضية جديدة، أو كمّل بالموجود.");
    return 0;
}

// ---------- 3) فرع تاني (للتحويل بين الفروع وكاشير فرع تاني) ----------
var branch2Id = branches.Skip(1).Select(b => (Guid?)Id(b, "id")).FirstOrDefault()
    ?? Id(await Post("/api/v1/branches", new
    {
        name = "فرع 2", code = "BR2", phoneNumber = (string?)null, street = (string?)null,
        city = "عمّان", postalCode = (string?)null, country = "الأردن"
    }), "branchId");
Step("فرع 2");

// ---------- 4) مستخدمين ----------
var roles = Items(await Get("/api/v1/users/roles")).ToList();
Guid Role(string name) => Id(roles.Single(r => r.GetProperty("name").GetString() == name), "id");

async Task CreateUser(string fullName, string username, string role, Guid branchId)
{
    await Post("/api/v1/users", new
    {
        fullName, username, email = $"{username}@test.local", password = Password,
        roleId = Role(role), branchId
    });
    Step($"مستخدم {username} / {Password} ({role})");
}

await CreateUser("كاشير الفرع الرئيسي", "cashier1", "كاشير", mainBranchId);
await CreateUser("كاشير فرع 2", "cashier2", "كاشير", branch2Id);
await CreateUser("مساعد الأدمن", "assistant1", "مساعد أدمن", mainBranchId);
await CreateUser("سائق التوصيل", "driver1", "سائق", mainBranchId);

// ---------- 5) كتالوج ----------
var categoryId = Id(await Post("/api/v1/product-categories", new { name = MarkerCategory, parentCategoryId = (Guid?)null }), "categoryId");

async Task<(Guid ProductId, Dictionary<decimal, Guid> Units)> CreateProduct(
    string name, decimal price, (string Unit, decimal Factor, string Barcode)[] units,
    bool batchTracked = false, decimal minimumStock = 0m)
{
    var productId = Id(await Post("/api/v1/products", new
    {
        name, description = (string?)null, categoryId, isBatchTracked = batchTracked,
        suggestedRetailPrice = (decimal?)null, expectedShelfLifeDays = (int?)null,
        units = units.Select(u => new { unitName = u.Unit, conversionFactorToBase = u.Factor, isBaseUnit = u.Factor == 1m }).ToArray(),
        barcodes = units.Select(u => new { barcodeValue = u.Barcode, unitName = u.Unit }).ToArray()
    }), "productId");

    // §1.8: بلا ربط صريح بالفرع وسعر، المنتج مش مرئي للكاشير.
    await Post($"/api/v1/products/{productId}/branches",
        new { branchId = mainBranchId, sellingPrice = price, minimumStock, maximumStock = (decimal?)null });

    var unitIds = Items(await Get($"/api/v1/products/{productId}/units"))
        .ToDictionary(u => u.GetProperty("conversionFactorToBase").GetDecimal(), u => Id(u, "id"));
    Step($"منتج {name} ({price:0.000}) - {string.Join("، ", units.Select(u => $"{u.Unit}: {u.Barcode}"))}");
    return (productId, unitIds);
}

var milk = await CreateProduct("حليب طازج 1 لتر", 1.250m, [("حبة", 1m, "6250000000011"), ("كرتونة", 12m, "6250000000028")]);
var rice = await CreateProduct("رز 1 كغ", 3.500m, [("كيس", 1m, "6250000000035")]);
var water = await CreateProduct("مياه 1.5 لتر", 0.350m, [("قنينة", 1m, "6250000000042")]);
var yogurt = await CreateProduct("لبن رائب 1 كغ", 1.900m, [("علبة", 1m, "6250000000059")], batchTracked: true);
var chips = await CreateProduct("شيبس صغير", 0.250m, [("كيس", 1m, "6250000000066")], minimumStock: 20m);

await Post($"/api/v1/products/{milk.ProductId}/complimentary-allowed", new { allowed = true });
Step("الحليب مسموح ضيافة");

// الحليب والرز بالفرع 2 كمان (بسعر مختلف) - بلا مخزون هناك، بيوصله بالتحويل.
await Post($"/api/v1/products/{milk.ProductId}/branches", new { branchId = branch2Id, sellingPrice = 1.300m, minimumStock = 0m, maximumStock = (decimal?)null });
await Post($"/api/v1/products/{rice.ProductId}/branches", new { branchId = branch2Id, sellingPrice = 3.600m, minimumStock = 0m, maximumStock = (decimal?)null });
Step("الحليب (1.300) والرز (3.600) مربوطين بفرع 2");

// ---------- 6) مورد + فاتورة شراء (مخزون + تكلفة) ----------
var supplierId = Id(await Post("/api/v1/suppliers", new
{
    name = "شركة التوريدات التجريبية", contactName = "أبو أحمد", phone = "0791234567", email = (string?)null,
    street = (string?)null, city = "عمّان", postalCode = (string?)null, country = "الأردن"
}), "supplierId");
Step("مورد");

object Line((Guid ProductId, Dictionary<decimal, Guid> Units) product, decimal quantity, decimal unitCost,
    string? batchNumber = null, DateOnly? expiry = null) => new
{
    productId = product.ProductId, productUnitId = product.Units[1m], quantity, unitCost,
    existingProductBatchId = (Guid?)null, newBatchNumber = batchNumber, newBatchExpiryDate = expiry
};

var purchase = await Post("/api/v1/purchase-invoices", new
{
    branchId = mainBranchId, supplierId, supplierInvoiceReference = "SUP-TEST-001",
    items = new[]
    {
        Line(milk, 48m, 0.800m),
        Line(rice, 20m, 2.600m),
        Line(water, 60m, 0.200m),
        Line(yogurt, 10m, 1.300m, "L-001", today.AddDays(5)),
        Line(chips, 5m, 0.150m)
    },
    imageReferences = (string[]?)null,
    dueDate = today.AddDays(7)
});
Step($"فاتورة شراء {purchase.GetProperty("totalAmount").GetDecimal():0.000} (غير مدفوعة، استحقاق بعد 7 أيام)");

// ---------- 7) عرض كمية ----------
await Post($"/api/v1/products/{water.ProductId}/promotions", new
{
    title = "3 مياه بـ 0.900", bundleQuantity = 3, bundlePrice = 0.900m, maxQuantityPerInvoice = (decimal?)null,
    startAtUtc = DateTime.UtcNow.AddHours(-1), endAtUtc = DateTime.UtcNow.AddDays(60),
    branchIds = new[] { mainBranchId }
});
Step("عرض: 3 مياه بـ 0.900 (بدل 1.050) بالفرع الرئيسي");

Console.WriteLine($"""

خلص. الملخّص:
  الفرع الرئيسي: {mainBranch.GetProperty("name").GetString()} ({mainBranchId})
  فرع 2: {branch2Id}
  المستخدمين (كلهم كلمة السر {Password}): admin، cashier1 (الرئيسي)، cashier2 (فرع 2)، assistant1، driver1
  المخزون بالرئيسي: حليب 48، رز 20، مياه 60، لبن 10 (دفعة L-001 بتخلص {today.AddDays(5):yyyy-MM-dd})، شيبس 5 (تحت الحد 20)
  التكلفة: حليب 0.800، رز 2.600، مياه 0.200، لبن 1.300، شيبس 0.150
  فاتورة الشراء: {purchase.GetProperty("totalAmount").GetDecimal():0.000} دين للمورد (مستحقة {today.AddDays(7):yyyy-MM-dd})
""");
return 0;
