using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Application.Inventory.RecordWasteIssue;
using SupermarketSystem.Application.Reporting.GetWasteLog;
using SupermarketSystem.Domain.Inventory;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Reporting;

[Collection(DatabaseCollection.Name)]
public sealed class GetWasteLogTests : IntegrationTestBase
{
    public GetWasteLogTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task حركة_تلف_مسجَّلة_تظهر_بالسجل_بسببها_الصحيح()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج سجل التلف");
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 20m);

        var issueHandler = scope.ServiceProvider.GetRequiredService<RecordWasteIssueHandler>();
        var issueResult = await issueHandler.HandleAsync(
            new RecordWasteIssueCommand(product.Id, unit.Id, Fixture.TestBranchId, 3m, WasteReason.Expired, "دفعة قديمة"),
            CancellationToken.None);
        Assert.True(issueResult.IsSuccess);

        var reportHandler = scope.ServiceProvider.GetRequiredService<GetWasteLogHandler>();
        var result = await reportHandler.HandleAsync(
            new GetWasteLogQuery(new PagedRequest(), Fixture.TestBranchId), CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal("منتج سجل التلف", item.ProductName);
        Assert.Equal(3m, item.QuantityBase);
        Assert.Equal(WasteReason.Expired, item.Reason);
        Assert.Equal("دفعة قديمة", item.Notes);
        Assert.False(item.NeedsReview);
        Assert.False(item.IsReplacedBySupplier);
    }

    [Fact]
    public async Task سجل_الضيافة_لا_يظهر_بسجل_التلف_والعكس()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج فصل السجلات", isComplimentaryAllowed: true);
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 20m);

        var complimentaryHandler = scope.ServiceProvider
            .GetRequiredService<SupermarketSystem.Application.Inventory.RecordComplimentaryIssue.RecordComplimentaryIssueHandler>();
        await complimentaryHandler.HandleAsync(
            new SupermarketSystem.Application.Inventory.RecordComplimentaryIssue.RecordComplimentaryIssueCommand(
                product.Id, unit.Id, Fixture.TestBranchId, 2m, "ضيافة"),
            CancellationToken.None);

        var reportHandler = scope.ServiceProvider.GetRequiredService<GetWasteLogHandler>();
        var result = await reportHandler.HandleAsync(
            new GetWasteLogQuery(new PagedRequest(), Fixture.TestBranchId), CancellationToken.None);

        Assert.Empty(result.Items);
    }
}
