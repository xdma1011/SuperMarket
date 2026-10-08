using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using SupermarketSystem.API.Common;
using SupermarketSystem.API.Endpoints;
using SupermarketSystem.Application;
using SupermarketSystem.Infrastructure;
using SupermarketSystem.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddAppRateLimiting(builder.Configuration);

// سجل بملف يومي (2-أ بند 4) - بلا حزمة جديدة، راجع DailyFileLoggerProvider. المجلد الافتراضي `logs` جنب الـAPI.
if (builder.Configuration.GetValue("Logging:File:Enabled", true))
{
    var logDirectory = builder.Configuration["Logging:File:Directory"];
    if (string.IsNullOrWhiteSpace(logDirectory))
    {
        logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
    }

    builder.Logging.AddProvider(new DailyFileLoggerProvider(logDirectory, builder.Configuration.GetValue("Logging:File:RetentionDays", 30)));
    // أوامر SQL كلها (Information) بتغرق الملف بلا فايدة - الأخطاء والتحذيرات بس من EF. قابل للتجاوز بـLogging:File:LogLevel.
    builder.Logging.AddFilter<DailyFileLoggerProvider>("Microsoft.EntityFrameworkCore", LogLevel.Warning);
}

// يسمح إرسال الـenums كنص ("Admin") لا رقم فقط (0/1) — الفرونت إند
// (Angular) بيرسل قيم enum كنصوص مطابقة لأسمائها بـC# بالضبط، وبلا هذا
// الإعداد، محرك System.Text.Json الافتراضي بيرفض أي enum مُرسَل كنص
// برسالة JsonException غامضة (زي اللي كسرت /auth/login).
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddHealthChecks()
    // Verifies the database is actually reachable, not merely that the
    // process is alive — a health check that only returns 200 tells an
    // orchestrator nothing useful.
    .AddDbContextCheck<AppDbContext>("database");

builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();

// === Swagger ===
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Supermarket System API", Version = "v1" });

    // يسمح تجربة endpoints المحمية مباشرة من واجهة Swagger — تلصق
    // "Bearer {access_token}" مرة وحدة عبر زر Authorize، وينطبق تلقائيًا
    // على كل استدعاء بعدها.
    var scheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "أدخل التوكن بصيغة: Bearer {access_token}"
    };
    options.AddSecurityDefinition("Bearer", scheme);
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }, Array.Empty<string>() }
    });
});

// === CORS ===
// اسم Policy صريح (لا AllowAll مجهول) — يسهّل لاحقًا إضافة origins تانية
// (لوحة إدارة بدومين إنتاج حقيقي) بلا تغيير طريقة التسجيل، بس تعديل
// القائمة. مقصود محصور بـorigins معروفة صراحةً، لا أي origin (*) —
// خصوصًا إن الـAPI بيستقبل Authorization header حقيقي، فتح CORS بالكامل
// هون كان رح يكون خطأ أمني حقيقي، لا تسهيل تطوير بريء.
const string AdminWebCorsPolicy = "AdminWebCorsPolicy";

builder.Services.AddCors(options =>
{
    options.AddPolicy(AdminWebCorsPolicy, policy =>
    {
        policy
            .WithOrigins(
                "http://localhost:4200",
                "https://localhost:4200")
            .AllowAnyHeader()
            .AllowAnyMethod();
        // بلا AllowCredentials(): المصادقة هون عبر Authorization header
        // (Bearer token) لا كوكيز، فما في داعي نسمح بإرسال كوكيز عبر
        // origins مختلفة — تفعيلها بلا حاجة فعلية كان رح يوسّع سطح الهجوم
        // بلا أي فائدة.
    });
});

var app = builder.Build();

// بلا شرط IsDevelopment() عمدًا: المشروع كله محلي على جهازك (SQL Server
// محلي، بلا نشر سحابي أو بيئة إنتاج فعلية - راجع CLAUDE.md)، فمافي داعي
// شرط بيئة كان بيسبب لخبطة "وين الـSwagger" كل ما ASPNETCORE_ENVIRONMENT
// ما يكون مضبوط بالضبط بالطريقة اللي تشغّل فيها المشروع.
app.UseSwagger();
app.UseSwaggerUI();

// Order matters: correlation id first, so the exception handler's logs
// already carry it.
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<ClientVersionMiddleware>();

// CORS لازم يسبق المصادقة والتخويل — طلبات preflight (OPTIONS) لازم
// تنعالج وترجع هيدرات CORS الصحيحة قبل ما توصل لأي فحص هوية أو صلاحية.
app.UseCors(AdminWebCorsPolicy);

// حد الطلبات (2-أ بند 3): بعد CORS (ردود الـ429 تحمل هيدرات CORS)، وقبل المصادقة (ما بدنا نصرف جهد على طلب مرفوض أصلًا).
app.UseRateLimiter();

// الترتيب هون إلزامي: UseAuthentication (مين أنت؟) لازم تسبق
// UseAuthorization (مسموحلك؟) — عكسهم بيخلي التخويل يشتغل على هوية
// فاضية دائمًا.
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapSystemEndpoints();
app.MapAuthenticationEndpoints();
app.MapUserEndpoints();
app.MapInventoryAdjustmentEndpoints();
app.MapReviewsEndpoints();
app.MapCashierSyncEndpoints();
app.MapBranchEndpoints();
app.MapCatalogEndpoints();
app.MapUnitOfMeasureEndpoints();
app.MapStockTransferEndpoints();
app.MapSupplierEndpoints();
app.MapPurchasingEndpoints();
app.MapPurchaseInvoiceDraftEndpoints();
app.MapSalesEndpoints();
app.MapSaleAssistEndpoints();
app.MapOrderingEndpoints();
app.MapCustomerEndpoints();
app.MapCouponEndpoints();
app.MapCustomerAuthEndpoints();
app.MapPublicCatalogEndpoints();
app.MapDriverEndpoints();
app.MapReturnEndpoints();
app.MapCashManagementEndpoints();
app.MapStocktakeEndpoints();
app.MapNotificationEndpoints();
app.MapBackupEndpoints();
app.MapReportingEndpoints();
app.MapFinanceEndpoints();
app.MapPartnersEndpoints();
app.MapEmployeeEndpoints();

// فحص Migrations ناقصة عند الإقلاع (2-أ بند 1، 8/10/2026): نسيان `dotnet ef database update` بعد تحديث كان بيطلع 500 غامضة بنص
// الاستخدام. هون بس بنكتب رسالة واضحة بالسجل (ما بنطبّق شي تلقائيًا - قاعدة صاحب المشروع الحقيقية، هو بيطبّقها يدويًا §1.4).
// بالخلفية بعد الإقلاع: ما بيأخّر فتح الـAPI، وفشل الفحص نفسه (القاعدة مش متاحة) ما بيوقف شي.
app.Lifetime.ApplicationStarted.Register(() => _ = Task.Run(async () =>
{
    var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("MigrationsCheck");
    try
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        if (pending.Count > 0)
        {
            logger.LogError(
                "في {Count} Migration ما انطبقت على القاعدة: {Migrations}. شغّل: dotnet ef database update --project SupermarketSystem.Infrastructure --startup-project SupermarketSystem.API " +
                "(أو update-database.bat) وإلا بعض الشاشات رح تطلع 500.",
                pending.Count, string.Join(", ", pending));
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "تعذّر فحص Migrations الناقصة (القاعدة مش متاحة؟).");
    }
}));

app.Run();

// STILL NOT IMPLEMENTED (deliberate, tracked):
//   - HTTPS redirection enforcement — deployment concerns,
//     added with the hosting decision rather than guessed at now.
//   - CORS origins are hardcoded to localhost:4200 for local development.
//     A production deployment must move this list into configuration
//     (appsettings/environment variables) rather than editing this file.

// Top-level statements generate an *internal* Program class by default —
// WebApplicationFactory<Program> (SupermarketSystem.IntegrationTests) needs
// it public to reference it from another assembly. Marker only, zero effect
// on runtime behavior.
public partial class Program { }
