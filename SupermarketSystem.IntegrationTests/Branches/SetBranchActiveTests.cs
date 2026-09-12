using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Branches.CreateBranch;
using SupermarketSystem.Application.Branches.SetBranchActive;
using SupermarketSystem.Application.Common.Results;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Branches;

[Collection(DatabaseCollection.Name)]
public sealed class SetBranchActiveTests : IntegrationTestBase
{
    public SetBranchActiveTests(DatabaseFixture fixture) : base(fixture) { }

    private async Task<Guid> SeedBranchAsync(IServiceScope scope)
    {
        var handler = scope.ServiceProvider.GetRequiredService<CreateBranchHandler>();
        var code = $"ACT{Guid.NewGuid():N}"[..12];
        var result = await handler.HandleAsync(
            new CreateBranchCommand("فرع للإيقاف", code, null, null, null, null, null), CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Value.BranchId;
    }

    [Fact]
    public async Task إيقاف_فرع_ينجح_بلا_حذف_فعلي()
    {
        using var scope = CreateScope();
        var branchId = await SeedBranchAsync(scope);
        var handler = scope.ServiceProvider.GetRequiredService<SetBranchActiveHandler>();

        var result = await handler.HandleAsync(new SetBranchActiveCommand(branchId, false), CancellationToken.None);

        Assert.True(result.IsSuccess);

        var db = CreateDbContext(scope);
        var branch = await db.Branches.AsNoTracking().SingleAsync(b => b.Id == branchId);
        Assert.False(branch.IsActive);
    }

    [Fact]
    public async Task تفعيل_فرع_موقوف_يعيده_فعالًا()
    {
        using var scope = CreateScope();
        var branchId = await SeedBranchAsync(scope);
        var handler = scope.ServiceProvider.GetRequiredService<SetBranchActiveHandler>();

        await handler.HandleAsync(new SetBranchActiveCommand(branchId, false), CancellationToken.None);
        var result = await handler.HandleAsync(new SetBranchActiveCommand(branchId, true), CancellationToken.None);

        Assert.True(result.IsSuccess);

        var db = CreateDbContext(scope);
        var branch = await db.Branches.AsNoTracking().SingleAsync(b => b.Id == branchId);
        Assert.True(branch.IsActive);
    }

    [Fact]
    public async Task إيقاف_فرع_غير_موجود_يفشل_بـNotFound()
    {
        using var scope = CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<SetBranchActiveHandler>();

        var result = await handler.HandleAsync(new SetBranchActiveCommand(Guid.NewGuid(), false), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
        Assert.Equal("Branch.NotFound", result.Error.Code);
    }
}
