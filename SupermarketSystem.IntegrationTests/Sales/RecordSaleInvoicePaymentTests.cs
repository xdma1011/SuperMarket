using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Application.Sales.CompleteSale;
using SupermarketSystem.Application.Sales.GetCustomerDebts;
using SupermarketSystem.Application.Sales.RecordSaleInvoicePayment;
using SupermarketSystem.Domain.CashManagement;
using SupermarketSystem.Infrastructure.Persistence;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Sales;

/// <summary>
/// RecordSaleInvoicePaymentHandler — "بيع بالدين" (CLAUDE.md، طلب صاحب
/// المشروع الصريح 22/9/2026). GetCustomerDebtsHandler يُختبَر هون أيضًا
/// لأنه مبني مباشرة فوق نتيجة هذه العملية (الدين المتبقي)، نفس نمط
/// RecordPurchaseInvoicePaymentTests بالضبط.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class RecordSaleInvoicePaymentTests : IntegrationTestBase
{
    public RecordSaleInvoicePaymentTests(DatabaseFixture fixture) : base(fixture) { }

    /// <summary>
    /// Scope خاص فيها لحالها - نفس سبب CreateAPurchaseInvoiceAsync بالضبط
    /// (تفادي RowVersion زائف الفشل لما نفس الـContext يعمل عملية ثانية
    /// على نفس الصف بعد عملية أولى بنفس الـScope).
    /// </summary>
    private async Task<Guid> CreateACreditSaleAsync(decimal sellingPrice = 100m, decimal paidNow = 0m)
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج بيع بالدين");
        await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, sellingPrice);
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 10m);
        var customer = await TestDataBuilder.CreateCustomerAsync(db);

        var payments = paidNow > 0
            ? new[] { new CompleteSalePaymentDto(TestDataBuilder.CashPaymentMethodId, paidNow, null, Guid.NewGuid()) }
            : Array.Empty<CompleteSalePaymentDto>();

        var handler = scope.ServiceProvider.GetRequiredService<CompleteSaleHandler>();
        var result = await handler.HandleAsync(
            new CompleteSaleCommand(
                Fixture.TestBranchId, Guid.NewGuid(), customer.Id, 0m,
                new[] { new CompleteSaleItemDto(product.Id, unit.Id, 1m, 0m, null) },
                payments),
            CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Value.SaleInvoiceId;
    }

    [Fact]
    public async Task بيع_بالدين_لزبون_معروف_بلا_أي_دفعة_ينجح()
    {
        var saleInvoiceId = await CreateACreditSaleAsync(sellingPrice: 100m, paidNow: 0m);

        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var invoice = await db.SaleInvoices.AsNoTracking().FirstAsync(s => s.Id == saleInvoiceId);
        Assert.Equal(100m, invoice.TotalAmount);
        Assert.Equal(0m, invoice.TotalPaidAmount);
    }

    [Fact]
    public async Task بيع_بدفعة_جزئية_لزبون_معروف_ينجح_والباقي_دين()
    {
        var saleInvoiceId = await CreateACreditSaleAsync(sellingPrice: 100m, paidNow: 30m);

        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var invoice = await db.SaleInvoices.AsNoTracking().FirstAsync(s => s.Id == saleInvoiceId);
        Assert.Equal(100m, invoice.TotalAmount);
        Assert.Equal(30m, invoice.TotalPaidAmount);
    }

    [Fact]
    public async Task بيع_بدفعة_أكبر_من_الإجمالي_يفشل_حتى_لو_لزبون_معروف()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج دفع زائد");
        await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, 50m);
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 10m);
        var customer = await TestDataBuilder.CreateCustomerAsync(db);

        var handler = scope.ServiceProvider.GetRequiredService<CompleteSaleHandler>();
        var result = await handler.HandleAsync(
            new CompleteSaleCommand(
                Fixture.TestBranchId, Guid.NewGuid(), customer.Id, 0m,
                new[] { new CompleteSaleItemDto(product.Id, unit.Id, 1m, 0m, null) },
                new[] { new CompleteSalePaymentDto(TestDataBuilder.CashPaymentMethodId, 999m, null, Guid.NewGuid()) }),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Sale.PaymentsExceedTotal", result.Error!.Code);
    }

    [Fact]
    public async Task تسجيل_دفعة_من_زبون_يقلّل_الدين_ويسجّل_حركة_دخول_بالدرج()
    {
        var saleInvoiceId = await CreateACreditSaleAsync(sellingPrice: 100m, paidNow: 0m);

        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);

        var handler = scope.ServiceProvider.GetRequiredService<RecordSaleInvoicePaymentHandler>();
        var result = await handler.HandleAsync(
            new RecordSaleInvoicePaymentCommand(saleInvoiceId, TestDataBuilder.CashPaymentMethodId, 40m, null, Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(40m, result.Value.NewTotalPaidAmount);
        Assert.Equal(60m, result.Value.RemainingDebt);

        var cashLog = await db.CashDrawerLogs.AsNoTracking()
            .FirstAsync(l => l.MovementType == CashDrawerMovementType.SaleCashIn
                && l.ReferenceType == CashDrawerReferenceType.SaleInvoicePayment);
        Assert.Equal(40m, cashLog.Amount);
    }

    [Fact]
    public async Task دفعة_تتجاوز_إجمالي_فاتورة_البيع_تفشل_بخطأ_تحقق()
    {
        var saleInvoiceId = await CreateACreditSaleAsync(sellingPrice: 50m, paidNow: 0m);

        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);

        var handler = scope.ServiceProvider.GetRequiredService<RecordSaleInvoicePaymentHandler>();
        var result = await handler.HandleAsync(
            new RecordSaleInvoicePaymentCommand(saleInvoiceId, TestDataBuilder.CashPaymentMethodId, 999m, null, Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Equal("Payment.ExceedsInvoiceTotal", result.Error.Code);
    }

    [Fact]
    public async Task إرسال_نفس_ClientRequestId_مرتين_لدفعة_الزبون_يفشل_بتعارض_ولا_يكرّرها()
    {
        var saleInvoiceId = await CreateACreditSaleAsync(sellingPrice: 100m, paidNow: 0m);

        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);

        var handler = scope.ServiceProvider.GetRequiredService<RecordSaleInvoicePaymentHandler>();
        var clientRequestId = Guid.NewGuid();

        var first = await handler.HandleAsync(
            new RecordSaleInvoicePaymentCommand(saleInvoiceId, TestDataBuilder.CashPaymentMethodId, 30m, null, clientRequestId),
            CancellationToken.None);
        Assert.True(first.IsSuccess);

        var second = await handler.HandleAsync(
            new RecordSaleInvoicePaymentCommand(saleInvoiceId, TestDataBuilder.CashPaymentMethodId, 30m, null, clientRequestId),
            CancellationToken.None);

        Assert.True(second.IsFailure);
        Assert.Equal(ErrorType.Conflict, second.Error!.Type);
        Assert.Equal("Payment.DuplicateRequest", second.Error.Code);

        var paymentCount = await db.SaleInvoicePayments.CountAsync(p => p.ClientRequestId == clientRequestId);
        Assert.Equal(1, paymentCount);
    }

    [Fact]
    public async Task دين_الزبون_ينعكس_بشاشة_الديون_وينخفض_بعد_الدفع()
    {
        var saleInvoiceId = await CreateACreditSaleAsync(sellingPrice: 100m, paidNow: 0m);

        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);

        var debtsHandler = scope.ServiceProvider.GetRequiredService<GetCustomerDebtsHandler>();
        var beforePayment = await debtsHandler.HandleAsync(CancellationToken.None);
        Assert.Single(beforePayment.Customers);
        Assert.Equal(100m, beforePayment.Customers[0].RemainingDebt);

        var paymentHandler = scope.ServiceProvider.GetRequiredService<RecordSaleInvoicePaymentHandler>();
        var payment = await paymentHandler.HandleAsync(
            new RecordSaleInvoicePaymentCommand(saleInvoiceId, TestDataBuilder.CashPaymentMethodId, 100m, null, Guid.NewGuid()),
            CancellationToken.None);
        Assert.True(payment.IsSuccess);

        var afterPayment = await debtsHandler.HandleAsync(CancellationToken.None);
        Assert.Empty(afterPayment.Customers);
    }
}
