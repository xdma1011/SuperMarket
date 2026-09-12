using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.CashManagement.CompleteCashClosing;
using SupermarketSystem.Application.CashManagement.GetCashClosings;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Application.Sales.CompleteSale;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.CashManagement;

/// <summary>
/// CompleteCashClosingHandler — المتوقع (ExpectedCash) يُحسب من
/// CashDrawerLog الكامل، لا من فواتير البيع فقط (راجع تعليق الـHandler).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class CompleteCashClosingTests : IntegrationTestBase
{
    public CompleteCashClosingTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task تقفيل_صندوق_بعد_بيعة_كاش_يحسب_المتوقع_بشكل_صحيح()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج تقفيل الصندوق");
        await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, 25m);
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 10m);

        var saleHandler = scope.ServiceProvider.GetRequiredService<CompleteSaleHandler>();
        var sale = await saleHandler.HandleAsync(
            new CompleteSaleCommand(
                Fixture.TestBranchId, Guid.NewGuid(), null, 0m,
                new[] { new CompleteSaleItemDto(product.Id, unit.Id, 2m, 0m, null) },
                new[] { new CompleteSalePaymentDto(TestDataBuilder.CashPaymentMethodId, 50m, null, Guid.NewGuid()) }),
            CancellationToken.None);
        Assert.True(sale.IsSuccess);

        var handler = scope.ServiceProvider.GetRequiredService<CompleteCashClosingHandler>();
        var result = await handler.HandleAsync(
            new CompleteCashClosingCommand(
                Fixture.TestBranchId, DateOnly.FromDateTime(DateTime.UtcNow), CountedCash: 50m,
                CountedDetails: Array.Empty<CompleteCashClosingCountDto>()),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(50m, result.Value.ExpectedCash);
        Assert.Equal(50m, result.Value.CountedCash);
        Assert.Equal(0m, result.Value.Variance);
    }

    [Fact]
    public async Task تقفيل_نفس_الفرع_ونفس_اليوم_مرتين_يفشل_بتعارض()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var handler = scope.ServiceProvider.GetRequiredService<CompleteCashClosingHandler>();
        var businessDate = DateOnly.FromDateTime(DateTime.UtcNow);

        var first = await handler.HandleAsync(
            new CompleteCashClosingCommand(Fixture.TestBranchId, businessDate, 0m, Array.Empty<CompleteCashClosingCountDto>()),
            CancellationToken.None);
        Assert.True(first.IsSuccess);

        var second = await handler.HandleAsync(
            new CompleteCashClosingCommand(Fixture.TestBranchId, businessDate, 0m, Array.Empty<CompleteCashClosingCountDto>()),
            CancellationToken.None);

        Assert.True(second.IsFailure);
        Assert.Equal(ErrorType.Conflict, second.Error!.Type);
        Assert.Equal("CashClosing.AlreadyClosed", second.Error.Code);
    }

    [Fact]
    public async Task تقفيل_بمبلغ_معدود_سالب_يفشل_بخطأ_تحقق()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var handler = scope.ServiceProvider.GetRequiredService<CompleteCashClosingHandler>();

        var result = await handler.HandleAsync(
            new CompleteCashClosingCommand(
                Fixture.TestBranchId, DateOnly.FromDateTime(DateTime.UtcNow), CountedCash: -1m,
                Array.Empty<CompleteCashClosingCountDto>()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Equal("CashClosing.CountedCashNegative", result.Error.Code);
    }

    [Fact]
    public async Task قائمة_تقفيلات_الصندوق_ترجّع_ما_تم_إنشاؤه_مع_اسم_الفرع()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var closeHandler = scope.ServiceProvider.GetRequiredService<CompleteCashClosingHandler>();
        var closeResult = await closeHandler.HandleAsync(
            new CompleteCashClosingCommand(Fixture.TestBranchId, DateOnly.FromDateTime(DateTime.UtcNow), 0m, Array.Empty<CompleteCashClosingCountDto>()),
            CancellationToken.None);
        Assert.True(closeResult.IsSuccess);

        var listHandler = scope.ServiceProvider.GetRequiredService<GetCashClosingsHandler>();
        var result = await listHandler.HandleAsync(
            new GetCashClosingsQuery(new PagedRequest(), Fixture.TestBranchId), CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal("فرع الاختبار", result.Items[0].BranchName);
    }
}
