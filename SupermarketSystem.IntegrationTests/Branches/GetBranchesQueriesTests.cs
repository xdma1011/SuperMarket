using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Branches.CreateBranch;
using SupermarketSystem.Application.Branches.GetBranches;
using SupermarketSystem.Application.Branches.GetPublicBranches;
using SupermarketSystem.Application.Branches.SetBranchActive;
using SupermarketSystem.Application.Common.Pagination;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Branches;

[Collection(DatabaseCollection.Name)]
public sealed class GetBranchesQueryTests : IntegrationTestBase
{
    public GetBranchesQueryTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task البحث_بالاسم_أو_الكود_يجد_الفرع()
    {
        using var scope = CreateScope();
        var createHandler = scope.ServiceProvider.GetRequiredService<CreateBranchHandler>();
        var code = $"SRCH{Guid.NewGuid():N}"[..12];
        var created = await createHandler.HandleAsync(
            new CreateBranchCommand("فرع البحث الفريد", code, null, null, null, null, null), CancellationToken.None);
        Assert.True(created.IsSuccess);

        var handler = scope.ServiceProvider.GetRequiredService<GetBranchesHandler>();
        var result = await handler.HandleAsync(
            new GetBranchesQuery(new PagedRequest { Search = code }), CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(created.Value.BranchId, item.Id);
    }
}

[Collection(DatabaseCollection.Name)]
public sealed class GetPublicBranchesQueryTests : IntegrationTestBase
{
    public GetPublicBranchesQueryTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task يرجع_الفروع_الفعالة_فقط_بلا_مصادقة()
    {
        using var scope = CreateScope();
        var createHandler = scope.ServiceProvider.GetRequiredService<CreateBranchHandler>();
        var code = $"PUB{Guid.NewGuid():N}"[..12];
        var created = await createHandler.HandleAsync(
            new CreateBranchCommand("فرع عام", code, null, null, null, null, null), CancellationToken.None);
        Assert.True(created.IsSuccess);

        var setActiveHandler = scope.ServiceProvider.GetRequiredService<SetBranchActiveHandler>();
        await setActiveHandler.HandleAsync(new SetBranchActiveCommand(created.Value.BranchId, false), CancellationToken.None);

        var handler = scope.ServiceProvider.GetRequiredService<GetPublicBranchesHandler>();
        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.Contains(result, b => b.Id == Fixture.TestBranchId);
        Assert.DoesNotContain(result, b => b.Id == created.Value.BranchId);
    }
}
