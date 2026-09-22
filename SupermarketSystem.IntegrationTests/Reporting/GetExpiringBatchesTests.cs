using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Application.Reporting.GetExpiringBatches;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Reporting;

[Collection(DatabaseCollection.Name)]
public sealed class GetExpiringBatchesTests : IntegrationTestBase
{
    public GetExpiringBatchesTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task دفعة_قريبة_من_الانتهاء_وبرصيد_موجب_تظهر_بالتقرير()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);

        var (product, _) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج قرب الصلاحية", isBatchTracked: true);
        var soonBatch = await TestDataBuilder.CreateBatchAsync(
            db, product.Id, Fixture.TestBranchId, "SOON-BATCH", unitCost: 1m,
            expiryDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)));
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 10m, soonBatch.Id);

        var farBatch = await TestDataBuilder.CreateBatchAsync(
            db, product.Id, Fixture.TestBranchId, "FAR-BATCH", unitCost: 1m,
            expiryDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(90)));
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 10m, farBatch.Id);

        var handler = scope.ServiceProvider.GetRequiredService<GetExpiringBatchesHandler>();
        var result = await handler.HandleAsync(
            new GetExpiringBatchesQuery(new PagedRequest(), Fixture.TestBranchId), CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal("SOON-BATCH", item.BatchNumber);
        Assert.InRange(item.DaysRemaining, 4, 5);
    }

    [Fact]
    public async Task دفعة_قرب_الانتهاء_بلا_رصيد_متبقٍّ_لا_تظهر_بالتقرير()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);

        var (product, _) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج دفعة فارغة", isBatchTracked: true);
        var emptyBatch = await TestDataBuilder.CreateBatchAsync(
            db, product.Id, Fixture.TestBranchId, "EMPTY-BATCH", unitCost: 1m,
            expiryDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)));
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 0m, emptyBatch.Id);

        var handler = scope.ServiceProvider.GetRequiredService<GetExpiringBatchesHandler>();
        var result = await handler.HandleAsync(
            new GetExpiringBatchesQuery(new PagedRequest(), Fixture.TestBranchId), CancellationToken.None);

        Assert.Empty(result.Items);
    }
}
