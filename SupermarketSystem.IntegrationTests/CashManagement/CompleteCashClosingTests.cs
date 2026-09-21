using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.CashManagement.CompleteCashClosing;
using SupermarketSystem.Application.CashManagement.GetCashClosings;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Application.Finance.GetMonthlyProfitStatement;
using SupermarketSystem.Application.Purchasing.CompletePurchaseInvoice;
using SupermarketSystem.Application.Reporting.GetSalesSummary;
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

    [Fact]
    public async Task تقفيلان_بنفس_اليوم_بورديتين_مختلفتين_ينجحان_معًا()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var handler = scope.ServiceProvider.GetRequiredService<CompleteCashClosingHandler>();
        var businessDate = DateOnly.FromDateTime(DateTime.UtcNow);

        var morningShift = await handler.HandleAsync(
            new CompleteCashClosingCommand(Fixture.TestBranchId, businessDate, 0m, Array.Empty<CompleteCashClosingCountDto>(), ShiftNumber: 1),
            CancellationToken.None);
        Assert.True(morningShift.IsSuccess);
        Assert.Equal(1, morningShift.Value.ShiftNumber);

        var eveningShift = await handler.HandleAsync(
            new CompleteCashClosingCommand(Fixture.TestBranchId, businessDate, 0m, Array.Empty<CompleteCashClosingCountDto>(), ShiftNumber: 2),
            CancellationToken.None);
        Assert.True(eveningShift.IsSuccess);
        Assert.Equal(2, eveningShift.Value.ShiftNumber);
    }

    [Fact]
    public async Task تقفيلان_بنفس_اليوم_ونفس_رقم_الوردية_يفشل_بتعارض()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var handler = scope.ServiceProvider.GetRequiredService<CompleteCashClosingHandler>();
        var businessDate = DateOnly.FromDateTime(DateTime.UtcNow);

        var first = await handler.HandleAsync(
            new CompleteCashClosingCommand(Fixture.TestBranchId, businessDate, 0m, Array.Empty<CompleteCashClosingCountDto>(), ShiftNumber: 1),
            CancellationToken.None);
        Assert.True(first.IsSuccess);

        var second = await handler.HandleAsync(
            new CompleteCashClosingCommand(Fixture.TestBranchId, businessDate, 0m, Array.Empty<CompleteCashClosingCountDto>(), ShiftNumber: 1),
            CancellationToken.None);

        Assert.True(second.IsFailure);
        Assert.Equal(ErrorType.Conflict, second.Error!.Type);
        Assert.Equal("CashClosing.AlreadyClosed", second.Error.Code);
    }

    [Fact]
    public async Task رقم_وردية_أقل_من_واحد_يفشل_بخطأ_تحقق()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var handler = scope.ServiceProvider.GetRequiredService<CompleteCashClosingHandler>();

        var result = await handler.HandleAsync(
            new CompleteCashClosingCommand(
                Fixture.TestBranchId, DateOnly.FromDateTime(DateTime.UtcNow), 0m,
                Array.Empty<CompleteCashClosingCountDto>(), ShiftNumber: 0),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Equal("CashClosing.ShiftNumberInvalid", result.Error.Code);
    }

    /// <summary>
    /// دورة كاملة حقيقية بالـHandlers الفعلية (لا بذر مباشر بقاعدة البيانات
    /// لأي خطوة): شراء من مورد (يحدّث المخزون تلقائيًا) → بيع كاش → التحقق
    /// من انعكاسه على ملخص المبيعات وكشف الربح الشهري → تقفيل صندوق يطابق
    /// الكاش الداخل فعليًا. يغطي التكامل بين 4 وحدات ما كانت مختبرة سوا
    /// بنفس السيناريو من قبل.
    /// </summary>
    [Fact]
    public async Task دورة_كاملة_شراء_من_مورد_ثم_بيع_تنعكس_على_المبيعات_والربح_وتقفيل_الصندوق()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);

        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج دورة كاملة");
        await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, sellingPrice: 10m);
        var supplier = await TestDataBuilder.CreateSupplierAsync(db);

        // 1) شراء من المورد - 20 وحدة بتكلفة 4 دنانير، عبر الـHandler
        //    الفعلي (بيحدّث Stock تلقائيًا، لا بذر مباشر).
        var purchaseHandler = scope.ServiceProvider.GetRequiredService<CompletePurchaseInvoiceHandler>();
        var purchase = await purchaseHandler.HandleAsync(
            new CompletePurchaseInvoiceCommand(
                Fixture.TestBranchId, supplier.Id, SupplierInvoiceReference: null,
                Items: new[] { new CompletePurchaseInvoiceItemDto(product.Id, unit.Id, 20m, 4m, null, null, null) }),
            CancellationToken.None);
        Assert.True(purchase.IsSuccess);

        // 2) بيع 5 وحدات كاش
        var saleHandler = scope.ServiceProvider.GetRequiredService<CompleteSaleHandler>();
        var sale = await saleHandler.HandleAsync(
            new CompleteSaleCommand(
                Fixture.TestBranchId, Guid.NewGuid(), CustomerId: null, InvoiceLevelDiscountAmount: 0m,
                Items: new[] { new CompleteSaleItemDto(product.Id, unit.Id, Quantity: 5m, ManualDiscountAmount: 0m, ProductBatchId: null) },
                Payments: new[] { new CompleteSalePaymentDto(TestDataBuilder.CashPaymentMethodId, 50m, null, Guid.NewGuid()) }),
            CancellationToken.None);
        Assert.True(sale.IsSuccess);
        Assert.Equal(50m, sale.Value.TotalAmount); // 5 × 10

        var now = DateTime.UtcNow;

        // 3) ينعكس على ملخص المبيعات
        var salesSummaryHandler = scope.ServiceProvider.GetRequiredService<GetSalesSummaryHandler>();
        var salesSummary = await salesSummaryHandler.HandleAsync(
            new GetSalesSummaryQuery(
                Fixture.TestBranchId, now.AddMinutes(-5), now.AddMinutes(5), CompareFromUtc: null, CompareToUtc: null),
            CancellationToken.None);
        Assert.Equal(50m, salesSummary.Period.TotalSales);
        Assert.Equal(50m, salesSummary.Period.NetRevenue);

        // 4) ينعكس على كشف الربح الشهري (تكلفة البضاعة = 5 × 4 = 20)
        var statementHandler = scope.ServiceProvider.GetRequiredService<GetMonthlyProfitStatementHandler>();
        var statement = await statementHandler.HandleAsync(
            new GetMonthlyProfitStatementQuery(Fixture.TestBranchId, now.Year, now.Month), CancellationToken.None);
        Assert.True(statement.IsSuccess);
        Assert.Equal(50m, statement.Value.TotalSales);
        Assert.Equal(20m, statement.Value.CostOfGoodsSold);
        Assert.Equal(30m, statement.Value.GrossProfit);
        Assert.Equal(0, statement.Value.ItemsExcludedNoCostHistory);

        // 5) تقفيل الصندوق - المتوقع لازم يطابق الكاش الداخل من البيع بالضبط
        var closingHandler = scope.ServiceProvider.GetRequiredService<CompleteCashClosingHandler>();
        var closing = await closingHandler.HandleAsync(
            new CompleteCashClosingCommand(
                Fixture.TestBranchId, DateOnly.FromDateTime(now), CountedCash: 50m,
                CountedDetails: Array.Empty<CompleteCashClosingCountDto>()),
            CancellationToken.None);

        Assert.True(closing.IsSuccess);
        Assert.Equal(50m, closing.Value.ExpectedCash);
        Assert.Equal(0m, closing.Value.Variance);
    }
}
