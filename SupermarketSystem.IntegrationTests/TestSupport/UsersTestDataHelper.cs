using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Users.CreateUser;

namespace SupermarketSystem.IntegrationTests;

/// <summary>
/// أدوار ثابتة مبذورة بالـmigrations نفسها (راجع IdentityConfigurations.cs
/// بـInfrastructure) - نفس المعرّفات بالضبط، لإنشاء مستخدمين باختبارات
/// أدوار مختلفة (كاشير/سائق/مساعد أدمن) بلا الحاجة نمرّ بشاشة إدارة أدوار.
/// </summary>
internal static class UsersTestDataHelper
{
    public static readonly Guid MasterAdminRoleId = Guid.Parse("50e6125a-cac0-4d82-a0b8-9f3c6fff59d7");
    public static readonly Guid CashierRoleId = Guid.Parse("f3b401c7-84f6-4a0f-9f17-b689979c5d8c");
    public static readonly Guid AssistantAdminRoleId = Guid.Parse("5d0b3578-417e-4706-ab9b-fc9a208b6642");
    public static readonly Guid DriverRoleId = Guid.Parse("6e8f9a1b-2c3d-4e5f-8a9b-1c2d3e4f5a6b");

    public const string DefaultPassword = "Test_User_P@ss123!";

    /// <summary>ينشئ مستخدمًا فعليًا (عبر CreateUserHandler الحقيقي) بدور وفرع مُحدَّدين، ويرجع معرّفه.</summary>
    public static async Task<Guid> CreateUserWithRoleAsync(
        IServiceProvider services, Guid branchId, Guid roleId, string usernamePrefix)
    {
        using var scope = services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<CreateUserHandler>();

        var username = $"{usernamePrefix}.{Guid.NewGuid():N}";
        var result = await handler.HandleAsync(new CreateUserCommand(
            FullName: $"مستخدم اختبار {usernamePrefix}",
            Username: username,
            Email: $"{Guid.NewGuid():N}@test.local",
            Password: DefaultPassword,
            RoleId: roleId,
            BranchId: branchId), CancellationToken.None);

        Xunit.Assert.True(result.IsSuccess, result.Error?.Message);
        return result.Value.UserId;
    }

    /// <summary>نفس CreateUserWithRoleAsync، بس بيرجع اسم المستخدم كمان (لتسجيل دخول لاحق).</summary>
    public static async Task<(Guid UserId, string Username)> CreateUserWithRoleReturningUsernameAsync(
        IServiceProvider services, Guid branchId, Guid roleId, string usernamePrefix)
    {
        using var scope = services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<CreateUserHandler>();

        var username = $"{usernamePrefix}.{Guid.NewGuid():N}";
        var result = await handler.HandleAsync(new CreateUserCommand(
            FullName: $"مستخدم اختبار {usernamePrefix}",
            Username: username,
            Email: $"{Guid.NewGuid():N}@test.local",
            Password: DefaultPassword,
            RoleId: roleId,
            BranchId: branchId), CancellationToken.None);

        Xunit.Assert.True(result.IsSuccess, result.Error?.Message);
        return (result.Value.UserId, username);
    }
}
