using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Application.Users.CreateUser;
using SupermarketSystem.Application.Users.GetRoles;
using SupermarketSystem.Application.Users.GetUsers;
using SupermarketSystem.Application.Users.UpdateUser;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Users;

/// <summary>User/Role/UserRole/UserBranch ليست IBranchOwned - اختبار Handler مباشر.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class UsersHandlerTests : IntegrationTestBase
{
    public UsersHandlerTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task إنشاء_مستخدم_ينجح_ويربطه_بالدور_والفرع_الصحيحين()
    {
        using var scope = CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<CreateUserHandler>();
        var db = CreateDbContext(scope);

        // ⚠️ ملاحظة بنية اختبار: جدول Users مستثنى صراحة من تصفير Respawn
        // بين الاختبارات (DatabaseFixture.TablesToIgnore) - أي مستخدم
        // نُنشئه هون يبقى بقاعدة البيانات المشتركة طول التشغيلة **وبين
        // تشغيلات dotnet test منفصلة كمان** (لا تصفير إطلاقًا لهذا
        // الجدول). البريد الإلكتروني عنده فهرس فريد بقاعدة البيانات، فلازم
        // يكون كل بريد هون Guid-based لا نصًا ثابتًا، وإلا تشغيلة ثانية
        // للاختبارات ترتطم بنفس البريد المتروك من تشغيلة سابقة (409/500).
        var command = new CreateUserCommand(
            "أحمد الموظف", $"ahmad.{Guid.NewGuid():N}", $"ahmad.{Guid.NewGuid():N}@test.local", "Passw0rd!",
            UsersTestDataHelper.CashierRoleId, Fixture.TestBranchId);

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsSuccess);

        var userRole = await db.UserRoles.SingleAsync(ur => ur.UserId == result.Value.UserId);
        Assert.Equal(UsersTestDataHelper.CashierRoleId, userRole.RoleId);

        var userBranch = await db.UserBranches.SingleAsync(ub => ub.UserId == result.Value.UserId);
        Assert.Equal(Fixture.TestBranchId, userBranch.BranchId);
        Assert.True(userBranch.IsDefault);
    }

    [Theory]
    [InlineData("", "username_ok", "pass123")]
    [InlineData("اسم", "", "pass123")]
    [InlineData("اسم", "username_ok", "ab")]
    public async Task إنشاء_مستخدم_ببيانات_ناقصة_يفشل_بخطأ_تحقق(string fullName, string username, string password)
    {
        using var scope = CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<CreateUserHandler>();

        var command = new CreateUserCommand(
            fullName, username, "x@test.local", password, UsersTestDataHelper.CashierRoleId, Fixture.TestBranchId);

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
    }

    [Fact]
    public async Task إنشاء_مستخدم_باسم_مستخدم_مكرر_يفشل_بـ409()
    {
        using var scope = CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<CreateUserHandler>();

        var username = $"dup.{Guid.NewGuid():N}";
        var first = await handler.HandleAsync(new CreateUserCommand(
            "أول", username, $"a.{Guid.NewGuid():N}@test.local", "Passw0rd!", UsersTestDataHelper.CashierRoleId, Fixture.TestBranchId),
            CancellationToken.None);
        Assert.True(first.IsSuccess);

        var second = await handler.HandleAsync(new CreateUserCommand(
            "ثاني", username, $"b.{Guid.NewGuid():N}@test.local", "Passw0rd!", UsersTestDataHelper.CashierRoleId, Fixture.TestBranchId),
            CancellationToken.None);

        Assert.True(second.IsFailure);
        Assert.Equal(ErrorType.Conflict, second.Error!.Type);
    }

    [Fact]
    public async Task إنشاء_مستخدم_بدور_أو_فرع_غير_موجودين_يفشل_بـNotFound()
    {
        using var scope = CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<CreateUserHandler>();

        var badRole = await handler.HandleAsync(new CreateUserCommand(
            "اسم", $"u.{Guid.NewGuid():N}", "e@test.local", "Passw0rd!", Guid.NewGuid(), Fixture.TestBranchId),
            CancellationToken.None);
        Assert.True(badRole.IsFailure);
        Assert.Equal(ErrorType.NotFound, badRole.Error!.Type);

        var badBranch = await handler.HandleAsync(new CreateUserCommand(
            "اسم", $"u.{Guid.NewGuid():N}", "e@test.local", "Passw0rd!", UsersTestDataHelper.CashierRoleId, Guid.NewGuid()),
            CancellationToken.None);
        Assert.True(badBranch.IsFailure);
        Assert.Equal(ErrorType.NotFound, badBranch.Error!.Type);
    }

    [Fact]
    public async Task تعديل_مستخدم_يغيّر_الدور_والفرع_فعليًا_ويبطل_كاش_صلاحياته()
    {
        var userId = await UsersTestDataHelper.CreateUserWithRoleAsync(
            Fixture.Factory.Services, Fixture.TestBranchId, UsersTestDataHelper.CashierRoleId, "update.test");

        using var scope = CreateScope();
        var db = CreateDbContext(scope);
        var cache = scope.ServiceProvider.GetRequiredService<IMemoryCache>();
        var cacheKey = $"userpermissions:{userId}";
        cache.Set(cacheKey, new[] { "Sales.Create" }, TimeSpan.FromMinutes(5));

        var newBranch = new SupermarketSystem.Domain.Branches.Branch("فرع تاني", $"BR{Guid.NewGuid():N}"[..8], null, null);
        db.Branches.Add(newBranch);
        await db.SaveChangesAsync();

        var handler = scope.ServiceProvider.GetRequiredService<UpdateUserHandler>();
        var command = new UpdateUserCommand(
            userId, "اسم محدَّث", $"updated.{Guid.NewGuid():N}@test.local", UsersTestDataHelper.AssistantAdminRoleId, newBranch.Id, IsActive: false);

        var result = await handler.HandleAsync(command, CancellationToken.None);
        Assert.True(result.IsSuccess);

        var updatedRole = await db.UserRoles.SingleAsync(ur => ur.UserId == userId);
        Assert.Equal(UsersTestDataHelper.AssistantAdminRoleId, updatedRole.RoleId);

        var updatedBranch = await db.UserBranches.SingleAsync(ub => ub.UserId == userId);
        Assert.Equal(newBranch.Id, updatedBranch.BranchId);

        var user = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == userId);
        Assert.False(user.IsActive);
        Assert.Equal("اسم محدَّث", user.FullName);

        // الكاش المخزَّن يدويًا فوق لازم ينمسح فعليًا - راجع تعليق UpdateUserHandler.
        Assert.False(cache.TryGetValue(cacheKey, out _));
    }

    [Fact]
    public async Task تعديل_مستخدم_غير_موجود_يفشل_بـNotFound()
    {
        using var scope = CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<UpdateUserHandler>();

        var result = await handler.HandleAsync(new UpdateUserCommand(
            Guid.NewGuid(), "اسم", "e@test.local", UsersTestDataHelper.CashierRoleId, Fixture.TestBranchId, true),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
    }

    [Fact]
    public async Task قائمة_المستخدمين_تعرض_اسم_الدور_والفرع_الافتراضي()
    {
        var userId = await UsersTestDataHelper.CreateUserWithRoleAsync(
            Fixture.Factory.Services, Fixture.TestBranchId, UsersTestDataHelper.CashierRoleId, "list.test");

        using var scope = CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<GetUsersHandler>();

        var result = await handler.HandleAsync(new GetUsersQuery(new PagedRequest { PageSize = 100 }), CancellationToken.None);

        var item = result.Items.Single(u => u.UserId == userId);
        Assert.Contains("كاشير", item.RoleNames);
        Assert.Equal(Fixture.TestBranchId, item.DefaultBranchId);
    }

    [Fact]
    public async Task قائمة_الأدوار_تتضمن_الأدوار_الثابتة_المبذورة()
    {
        using var scope = CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<GetRolesHandler>();

        var roles = await handler.HandleAsync(CancellationToken.None);

        Assert.Contains(roles, r => r.Id == UsersTestDataHelper.MasterAdminRoleId);
        Assert.Contains(roles, r => r.Id == UsersTestDataHelper.CashierRoleId);
        Assert.Contains(roles, r => r.Id == UsersTestDataHelper.DriverRoleId);
    }
}
