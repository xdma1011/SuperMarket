using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Application.Purchasing.CompletePurchaseInvoice;
using SupermarketSystem.Application.Purchasing.GetPurchaseInvoices;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Purchasing;

/// <summary>GetPurchaseInvoicesHandler — قائمة فواتير الشراء مع اسم المورد.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class GetPurchaseInvoicesTests : IntegrationTestBase
{
    public GetPurchaseInvoicesTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task قائمة_فواتير_الشراء_تعرض_اسم_المورد_والإجماليات()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج قائمة الشراء");
        var supplier = await TestDataBuilder.CreateSupplierAsync(db, "مورد قائمة الشراء");

        var completeHandler = scope.ServiceProvider.GetRequiredService<CompletePurchaseInvoiceHandler>();
        var completed = await completeHandler.HandleAsync(
            new CompletePurchaseInvoiceCommand(Fixture.TestBranchId, supplier.Id, "REF-1",
                new[] { new CompletePurchaseInvoiceItemDto(product.Id, unit.Id, 3m, 4m, null, null, null) }),
            CancellationToken.None);
        Assert.True(completed.IsSuccess);

        var listHandler = scope.ServiceProvider.GetRequiredService<GetPurchaseInvoicesHandler>();
        var result = await listHandler.HandleAsync(
            new GetPurchaseInvoicesQuery(new PagedRequest(), Fixture.TestBranchId), CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal("مورد قائمة الشراء", item.SupplierName);
        Assert.Equal(12m, item.TotalAmount);
        Assert.Equal(0m, item.TotalPaidAmount);
    }
}
