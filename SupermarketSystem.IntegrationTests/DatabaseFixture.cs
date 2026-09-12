using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Respawn;
using SupermarketSystem.Application.Authentication.Login;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Domain.Branches;
using SupermarketSystem.Domain.Identity;
using SupermarketSystem.Infrastructure.Persistence;

namespace SupermarketSystem.IntegrationTests;

/// <summary>
/// قاعدة بيانات اختبار واحدة مشتركة بكل تشغيلة اختبارات (SQL Server
/// بحاوية Docker منفصلة تمامًا عن قاعدة بيانات المستخدم الحقيقية —
/// راجع CLAUDE.md §1.1). تُبنى مرة وحدة: migrate بالـmigrations الفعلية
/// الموجودة بالمشروع (بلا أي SQL يدوي)، ثم بذر مستخدم اختبار Master Admin
/// وفرع اختبار ثابتين — يبقيان طول التشغيلة (مُستثنيان من التصفير بين
/// الاختبارات) لأن كل اختبار محتاجهم لتسجيل الدخول.
///
/// التصفير بين كل اختبار (Respawn) بيشمل كل الجداول الأخرى (منتجات، مبيعات،
/// مخزون، إلخ) — كل اختبار يبلش من قاعدة بيانات نظيفة من ناحية البيانات
/// التجارية، بلا الحاجة لإعادة تشغيل migrations كل مرة.
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    // اسم القاعدة قابل للتبديل عبر متغيّر بيئة (INTEGRATION_TEST_DB_NAME) —
    // بلا أي تعديل على هذا الملف. مفيد لتشغيل عدة تشغيلات اختبار متوازية
    // (worktrees مختلفة) بأمان ضد نفس حاوية SQL Server، كل واحدة بقاعدتها
    // المنطقية الخاصة، بلا تعارض على تصفير Respawn لبعضها.
    private static readonly string ConnectionString =
        $"Server=127.0.0.1,14330;Database={Environment.GetEnvironmentVariable("INTEGRATION_TEST_DB_NAME") ?? "sprmrkt_integration_tests"};User Id=sa;Password=Test_Only_P@ss123;TrustServerCertificate=True;";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private Respawner _respawner = null!;

    public CustomWebApplicationFactory Factory { get; private set; } = null!;

    public Guid AdminUserId { get; private set; }
    public Guid TestBranchId { get; private set; }
    public const string AdminUsername = "test.admin";
    public const string AdminPassword = "Test_Admin_P@ss123!";

    private static readonly Guid MasterAdminRoleId = Guid.Parse("50e6125a-cac0-4d82-a0b8-9f3c6fff59d7");

    public async Task InitializeAsync()
    {
        Factory = new CustomWebApplicationFactory(ConnectionString);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync();
            await SeedFixedDataAsync(scope.ServiceProvider);
        }

        _respawner = await Respawner.CreateAsync(ConnectionString, new RespawnerOptions
        {
            DbAdapter = DbAdapter.SqlServer,
            TablesToIgnore =
            [
                "__EFMigrationsHistory",
                // مرجعية مبذورة عبر HasData بالـmigrations نفسها — تصفيرها
                // بيمسحها نهائيًا بلا رجعة (Respawn ما بيعرف يعيد بذرها،
                // هذا شغل الـmigrations لا الاختبارات).
                "Permissions", "Roles", "RolePermissions",
                "PaymentMethods", "UnitOfMeasures",
                // مستخدم ودور وفرع الاختبار الثابتين المبذورين هون فوق.
                "Users", "UserRoles", "UserBranches", "Branches"
            ]
        });
    }

    private async Task SeedFixedDataAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var passwordHasher = services.GetRequiredService<IPasswordHasher>();

        var existingAdmin = await db.Set<User>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Username == AdminUsername);

        var existingBranch = await db.Set<Branch>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.Code == "TST");

        if (existingAdmin is not null && existingBranch is not null)
        {
            // مبذورة أصلًا من تشغيلة سابقة بنفس القاعدة (الجداول دي مستثناة من التصفير).
            AdminUserId = existingAdmin.Id;
            TestBranchId = existingBranch.Id;
            return;
        }

        var branch = new Branch("فرع الاختبار", "TST", address: null, phoneNumber: null);
        db.Set<Branch>().Add(branch);

        var admin = new User("مدير الاختبار", AdminUsername, "test.admin@local.invalid");
        admin.SetPasswordHash(passwordHasher.Hash(AdminPassword));
        db.Set<User>().Add(admin);

        await db.SaveChangesAsync();

        db.Set<UserRole>().Add(new UserRole(admin.Id, MasterAdminRoleId, branchId: null));
        db.Set<UserBranch>().Add(new UserBranch(admin.Id, branch.Id, isDefault: true));
        await db.SaveChangesAsync();

        AdminUserId = admin.Id;
        TestBranchId = branch.Id;
    }

    /// <summary>يصفّر كل البيانات التجارية بين الاختبارات (راجع TablesToIgnore).</summary>
    public Task ResetDatabaseAsync() => _respawner.ResetAsync(ConnectionString);

    /// <summary>
    /// HttpClient حقيقي مسجَّل دخول فعليًا (عبر /api/v1/auth/login الحقيقي،
    /// لا توكن مصطنع يدويًا) بصلاحيات Master Admin الكاملة — كافي لأي
    /// endpoint محمي تقريبًا. اختبارات صلاحيات أضيق (403) تبني توكن مستخدم
    /// أقل صلاحية بذاتها.
    /// </summary>
    public async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Username = AdminUsername,
            Password = AdminPassword,
            AppType = "Admin",
            BranchId = TestBranchId
        });

        response.EnsureSuccessStatusCode();
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login!.AccessToken);

        return client;
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
    }
}
