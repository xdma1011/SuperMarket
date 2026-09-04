using System.Text.Json.Serialization;
using Microsoft.OpenApi.Models;
using SupermarketSystem.API.Common;
using SupermarketSystem.API.Endpoints;
using SupermarketSystem.Application;
using SupermarketSystem.Infrastructure;
using SupermarketSystem.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

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

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Order matters: correlation id first, so the exception handler's logs
// already carry it.
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();

// CORS لازم يسبق المصادقة والتخويل — طلبات preflight (OPTIONS) لازم
// تنعالج وترجع هيدرات CORS الصحيحة قبل ما توصل لأي فحص هوية أو صلاحية.
app.UseCors(AdminWebCorsPolicy);

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
app.MapSupplierEndpoints();
app.MapPurchasingEndpoints();
app.MapPurchaseInvoiceDraftEndpoints();
app.MapSalesEndpoints();
app.MapOrderingEndpoints();
app.MapCustomerEndpoints();
app.MapReturnEndpoints();
app.MapCashManagementEndpoints();
app.MapStocktakeEndpoints();
app.MapNotificationEndpoints();
app.MapBackupEndpoints();
app.MapReportingEndpoints();

app.Run();

// STILL NOT IMPLEMENTED (deliberate, tracked):
//   - Rate limiting, HTTPS redirection enforcement — deployment concerns,
//     added with the hosting decision rather than guessed at now.
//   - CORS origins are hardcoded to localhost:4200 for local development.
//     A production deployment must move this list into configuration
//     (appsettings/environment variables) rather than editing this file.
