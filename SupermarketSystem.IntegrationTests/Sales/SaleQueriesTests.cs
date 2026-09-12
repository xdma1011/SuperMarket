using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Application.Sales.CompleteSale;
using SupermarketSystem.Application.Sales.GetSaleInvoiceById;
using SupermarketSystem.Application.Sales.GetSaleInvoices;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Sales;

/// <summary>GetSaleInvoicesHandler/GetSaleInvoiceByIdHandler — StatusTitle عربي جاهز (راجع CLAUDE.md §3.1).</summary>
[Collection(DatabaseCollection.Name)]
public sealed class SaleQueriesTests : IntegrationTestBase
{
    public SaleQueriesTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task جلب_فاتورة_بيع_بمعرّفها_يرجّع_التفاصيل_مع_StatusTitle_عربي()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج للاستعلام");
        await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, 15m);
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 10m);

        var saleHandler = scope.ServiceProvider.GetRequiredService<CompleteSaleHandler>();
        var sale = await saleHandler.HandleAsync(
            new CompleteSaleCommand(
                Fixture.TestBranchId, Guid.NewGuid(), null, 0m,
                new[] { new CompleteSaleItemDto(product.Id, unit.Id, 2m, 0m, null) },
                new[] { new CompleteSalePaymentDto(TestDataBuilder.CashPaymentMethodId, 30m, null, Guid.NewGuid()) }),
            CancellationToken.None);
        Assert.True(sale.IsSuccess);

        var queryHandler = scope.ServiceProvider.GetRequiredService<GetSaleInvoiceByIdHandler>();
        var result = await queryHandler.HandleAsync(new GetSaleInvoiceByIdQuery(sale.Value.SaleInvoiceId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("مكتملة", result.Value.StatusTitle);
        Assert.Equal(1, result.Value.StatusCode);
        Assert.Single(result.Value.Items);
        Assert.Equal("منتج للاستعلام", result.Value.Items[0].ProductName);
        Assert.Equal(30m, result.Value.TotalAmount);
    }

    [Fact]
    public async Task جلب_فاتورة_غير_موجودة_يفشل_بـNotFound()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var handler = scope.ServiceProvider.GetRequiredService<GetSaleInvoiceByIdHandler>();

        var result = await handler.HandleAsync(new GetSaleInvoiceByIdQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
    }

    [Fact]
    public async Task البحث_برقم_هاتف_الزبون_يرجّع_فواتيره()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var customer = await TestDataBuilder.CreateCustomerAsync(db, "زبون البحث", phone: "0791234567");
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج بحث الزبون");
        await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, 5m);
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 10m);

        var saleHandler = scope.ServiceProvider.GetRequiredService<CompleteSaleHandler>();
        var sale = await saleHandler.HandleAsync(
            new CompleteSaleCommand(
                Fixture.TestBranchId, Guid.NewGuid(), customer.Id, 0m,
                new[] { new CompleteSaleItemDto(product.Id, unit.Id, 1m, 0m, null) },
                new[] { new CompleteSalePaymentDto(TestDataBuilder.CashPaymentMethodId, 5m, null, Guid.NewGuid()) }),
            CancellationToken.None);
        Assert.True(sale.IsSuccess);

        var queryHandler = scope.ServiceProvider.GetRequiredService<GetSaleInvoicesHandler>();
        var paging = new PagedRequest { Search = "0791234567" };
        var result = await queryHandler.HandleAsync(new GetSaleInvoicesQuery(paging, Fixture.TestBranchId), CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal(sale.Value.SaleInvoiceId, result.Items[0].Id);
        Assert.Equal("زبون البحث", result.Items[0].CustomerName);
    }
}
