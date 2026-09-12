using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Branches.CreateBranch;
using SupermarketSystem.Application.Branches.UpdateBranch;
using SupermarketSystem.Application.Common.Results;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Branches;

/// <summary>
/// يبني فرعًا خاصًا بكل اختبار (بلا لمس Fixture.TestBranchId المشترك -
/// اختبارات تانية كتير معتمدة على بقاء اسمه/حالته كما هي).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class UpdateBranchTests : IntegrationTestBase
{
    public UpdateBranchTests(DatabaseFixture fixture) : base(fixture) { }

    private async Task<Guid> SeedBranchAsync(IServiceScope scope)
    {
        var handler = scope.ServiceProvider.GetRequiredService<CreateBranchHandler>();
        var code = $"UPD{Guid.NewGuid():N}"[..12];
        var result = await handler.HandleAsync(
            new CreateBranchCommand("فرع للتعديل", code, "0791111111", null, null, null, null), CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Value.BranchId;
    }

    [Fact]
    public async Task تعديل_اسم_وهاتف_فرع_ينجح_ويبقي_العنوان_كما_هو()
    {
        using var scope = CreateScope();
        var branchId = await SeedBranchAsync(scope);
        var handler = scope.ServiceProvider.GetRequiredService<UpdateBranchHandler>();

        var result = await handler.HandleAsync(new UpdateBranchCommand(branchId, "اسم جديد", "0792222222"), CancellationToken.None);

        Assert.True(result.IsSuccess);

        var db = CreateDbContext(scope);
        var branch = await db.Branches.AsNoTracking().SingleAsync(b => b.Id == branchId);
        Assert.Equal("اسم جديد", branch.Name);
        Assert.Equal("0792222222", branch.PhoneNumber);
        Assert.Null(branch.Address);
    }

    [Fact]
    public async Task تعديل_باسم_فاضي_يفشل_بخطأ_تحقق()
    {
        using var scope = CreateScope();
        var branchId = await SeedBranchAsync(scope);
        var handler = scope.ServiceProvider.GetRequiredService<UpdateBranchHandler>();

        var result = await handler.HandleAsync(new UpdateBranchCommand(branchId, "   ", null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Equal("Branch.NameRequired", result.Error.Code);
    }

    [Fact]
    public async Task تعديل_فرع_غير_موجود_يفشل_بـNotFound()
    {
        using var scope = CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<UpdateBranchHandler>();

        var result = await handler.HandleAsync(new UpdateBranchCommand(Guid.NewGuid(), "اسم", null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
        Assert.Equal("Branch.NotFound", result.Error.Code);
    }
}
