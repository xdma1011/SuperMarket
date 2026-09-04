using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Policies;
using SupermarketSystem.Infrastructure.Persistence;
using SupermarketSystem.Infrastructure.Persistence.Interceptors;
using SupermarketSystem.Infrastructure.Services;

namespace SupermarketSystem.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // PlaceholderCurrentUserContext بقي بالكود عمدًا (Services/InfrastructureServices.cs)
        // — مفيد للاختبارات، بس ما عاد الموجود بالتسجيل الفعلي. هذا
        // السطر هو نقطة التبديل اللي تفعّل عزل الفروع فعليًا لأول مرة
        // بكل المشروع.
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserContext, RealCurrentUserContext>();
        services.AddScoped<IPermissionChecker, CachedPermissionChecker>();
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services.AddScoped<AuditableEntitySaveChangesInterceptor>();

        services.AddDbContext<AppDbContext>((serviceProvider, options) =>
        {
            options.UseSqlServer(
                configuration.GetConnectionString("DefaultConnection"),
                sql =>
                {
                    sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                    // Transient-fault resilience for SQL Server. Note: with a
                    // retrying execution strategy, explicit transactions must
                    // be wrapped via CreateExecutionStrategy().ExecuteAsync —
                    // relevant to the sale/purchase/return transaction
                    // boundaries in later phases.
                    sql.EnableRetryOnFailure();
                });

            options.AddInterceptors(serviceProvider.GetRequiredService<AuditableEntitySaveChangesInterceptor>());

            // No lazy loading, per the Architecture Review's EF performance rules.
        });

        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<IDocumentNumberGenerator, DocumentNumberGenerator>();

        // Settings cache + POS policy. Scoped (not singleton) because
        // CachedSettingsProvider depends on the scoped AppDbContext for
        // cache misses; the IMemoryCache backing it is a singleton, so the
        // cache itself survives across requests as intended.
        services.AddMemoryCache();
        services.AddScoped<ISettingsProvider, CachedSettingsProvider>();
        services.AddScoped<IPosPolicyService, PosPolicyService>();

        // Sale-critical seams: atomic stock decrement and explicit
        // transaction boundaries. See IStockOperations for why these are
        // not expressible through IApplicationDbContext alone.
        services.AddScoped<IStockOperations, StockOperations>();
        services.AddScoped<ICatalogVersionService, SqlCatalogVersionService>();
        services.AddScoped<ISaleInvoiceOperations, SaleInvoiceOperations>();
        services.AddScoped<ITransactionalExecutor, TransactionalExecutor>();

        // مُرسِل تلغرام — HttpClient مخصص (typed client)، بلا base address
        // ثابت (الرابط الكامل يُبنى بالتوكن جوّا SendAsync نفسها).
        services.AddHttpClient<INotificationSender, TelegramNotificationSender>();

        // بوت تلغرام لتسجيل دخول الزبائن (OTP) — منفصل عن بوت تنبيهات
        // الإدارة أعلاه (راجع تعليق TelegramBotClient).
        services.AddHttpClient<ITelegramBotClient, TelegramBotClient>();

        // Firebase Push Notifications - typed HttpClient قياسي (بلا Singleton
        // مخصص لتفادي تعقيد دورة حياة HttpClientFactory - تخزين access
        // token المؤقت بالذاكرة يعمل ضمن نطاق كل طلب، راجع تعليق
        // FirebasePushNotificationSender).
        services.AddHttpClient<IPushNotificationSender, FirebasePushNotificationSender>();

        services.AddScoped<IBackupService, SqlServerBackupService>();
        services.AddHostedService<DailyBackupBackgroundService>();
        services.AddHostedService<PendingReviewEscalationBackgroundService>();

        // مزوّدو قراءة فاتورة الشراء بالذكاء الاصطناعي — بترتيب الأولوية
        // بالضبط: Gemini، ثم Gemini Flash، ثم Claude (خط دفاع أخير).
        // ترتيب التسجيل هون هو نفسه ترتيب IEnumerable<IInvoiceOcrProvider>
        // اللي الخطوة الجاية (FallbackInvoiceOcrService) رح تعتمد عليه —
        // بلا أي منطق ترتيب إضافي بالمستهلِك، الترتيب متضمَّن هون فقط.
        services.AddHttpClient<GeminiProInvoiceOcrProvider>();
        services.AddScoped<IInvoiceOcrProvider>(sp => sp.GetRequiredService<GeminiProInvoiceOcrProvider>());

        services.AddHttpClient<GeminiFlashInvoiceOcrProvider>();
        services.AddScoped<IInvoiceOcrProvider>(sp => sp.GetRequiredService<GeminiFlashInvoiceOcrProvider>());

        services.AddHttpClient<ClaudeInvoiceOcrProvider>();
        services.AddScoped<IInvoiceOcrProvider>(sp => sp.GetRequiredService<ClaudeInvoiceOcrProvider>());

        services.AddScoped<IImageStorageService, ImageSharpWebPStorageService>();

        // بلا حالة وآمن للاستخدام المتزامن — Singleton كافٍ.
        services.AddSingleton<IPasswordHasher, AspNetPasswordHasher>();

        // === المصادقة ===
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.AddSingleton<ITokenService, JwtTokenService>();

        // توكن هوية الزبون (بعد نجاح OTP) — يعيد استخدام نفس مفتاح التوقيع،
        // بلا حالة، Singleton كافٍ (راجع تعليق CustomerAuthTokenService).
        services.AddSingleton<ICustomerAuthTokenService, CustomerAuthTokenService>();

        // توكن QR ثابت لهوية الزبون (منع تلاعب رقم هاتف يدوي بالكاشير).
        services.AddSingleton<IQrTokenService, QrTokenService>();

        var jwtOptions = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

        // الوسيط بيتحقق من التوقيع والصلاحية قبل ما يوصل الطلب لأي handler.
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidAudience = jwtOptions.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(string.IsNullOrWhiteSpace(jwtOptions.SigningKey)
                            ? new string('0', 32) // placeholder فقط؛ JwtTokenService بيفشل صراحة لو المفتاح غير مُعدّ
                            : jwtOptions.SigningKey)),

                    // صفر تسامح زمني: الافتراضي بالمكتبة 5 دقائق، يعني توكن
                    // "منتهي" بيضل مقبولًا 5 دقائق إضافية — يناقض مباشرةً
                    // سبب اختيارنا عمرًا قصيرًا أصلًا.
                    ClockSkew = TimeSpan.Zero
                };
            });

        // FallbackPolicy لا MapGroup("").RequireAuthorization() — الأخيرة
        // كانت الخطة الأصلية، وتبيّن أثناء التنفيذ إنها لا تشتغل فعليًا:
        // MapGroup("") بيرجّع builder جديد بيطبّق بس على endpoints تتسجّل
        // من خلاله هو، لا بأثر رجعي على endpoints مسجَّلة أصلًا عبر
        // app.MapXxxEndpoints() (كل الـendpoints الحالية بالمشروع). هذا هو
        // الآلية المدعومة رسميًا من ASP.NET Core لتطبيق "مصادقة افتراضية
        // على كل شي" بغض النظر عن طريقة التسجيل — أي endpoint جديد يُنسى
        // منه AllowAnonymous صراحةً بيُرفض تلقائيًا، لا يبقى مفتوحًا بصمت.
        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });

        return services;
    }
}
