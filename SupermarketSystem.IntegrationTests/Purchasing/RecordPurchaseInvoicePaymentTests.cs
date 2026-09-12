using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Application.Purchasing.CompletePurchaseInvoice;
using SupermarketSystem.Application.Purchasing.GetSupplierDebts;
using SupermarketSystem.Application.Purchasing.RecordPurchaseInvoicePayment;
using SupermarketSystem.Domain.CashManagement;
using SupermarketSystem.Infrastructure.Persistence;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Purchasing;

/// <summary>
/// RecordPurchaseInvoicePaymentHandler — الاعتماد على PaymentMethod.AffectsCashDrawer
/// (سلوك، لا اسم/كود) لتقرير كتابة CashDrawerLog، ونفس Idempotency
/// المتعارف عليها بالنظام. GetSupplierDebtsHandler يُختبَر هون أيضًا لأنه
/// مبني مباشرة فوق نتيجة هذه العملية (الدين المتبقي).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class RecordPurchaseInvoicePaymentTests : IntegrationTestBase
{
    public RecordPurchaseInvoicePaymentTests(DatabaseFixture fixture) : base(fixture) { }

    /// <summary>
    /// Scope خاص فيها لحالها - راجع تعليق CompleteASaleAsync بـVoidSaleTests.cs
    /// لنفس السبب بالضبط (تفادي RowVersion زائف الفشل لما نفس الـContext
    /// يعمل عملية ثانية على نفس الصف بعد عملية أولى بنفس الـScope).
    /// </summary>
    private async Task<Guid> CreateAPurchaseInvoiceAsync(decimal totalCost = 100m)
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج دفعة شراء");
        var supplier = await TestDataBuilder.CreateSupplierAsync(db);

        var handler = scope.ServiceProvider.GetRequiredService<CompletePurchaseInvoiceHandler>();
        var result = await handler.HandleAsync(
            new CompletePurchaseInvoiceCommand(Fixture.TestBranchId, supplier.Id, null,
                new[] { new CompletePurchaseInvoiceItemDto(product.Id, unit.Id, 1m, totalCost, null, null, null) }),
            CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Value.PurchaseInvoiceId;
    }

    [Fact]
    public async Task تسجيل_دفعة_كاش_للمورد_يقلّل_الدين_ويسجّل_حركة_خروج_بالدرج()
    {
        var invoiceId = await CreateAPurchaseInvoiceAsync(totalCost: 100m);

        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);

        var handler = scope.ServiceProvider.GetRequiredService<RecordPurchaseInvoicePaymentHandler>();
        var result = await handler.HandleAsync(
            new RecordPurchaseInvoicePaymentCommand(invoiceId, TestDataBuilder.CashPaymentMethodId, 40m, null, Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(40m, result.Value.NewTotalPaidAmount);
        Assert.Equal(60m, result.Value.RemainingDebt);

        var cashLog = await db.CashDrawerLogs.AsNoTracking()
            .FirstAsync(l => l.MovementType == CashDrawerMovementType.PurchasePaymentCashOut);
        Assert.Equal(40m, cashLog.Amount);
    }

    [Fact]
    public async Task دفعة_تتجاوز_إجمالي_الفاتورة_تفشل_بخطأ_تحقق()
    {
        var invoiceId = await CreateAPurchaseInvoiceAsync(totalCost: 50m);

        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);

        var handler = scope.ServiceProvider.GetRequiredService<RecordPurchaseInvoicePaymentHandler>();
        var result = await handler.HandleAsync(
            new RecordPurchaseInvoicePaymentCommand(invoiceId, TestDataBuilder.CashPaymentMethodId, 999m, null, Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Equal("Payment.ExceedsInvoiceTotal", result.Error.Code);
    }

    [Fact]
    public async Task إرسال_نفس_ClientRequestId_مرتين_للدفعة_يفشل_بتعارض_ولا_يكرّرها()
    {
        var invoiceId = await CreateAPurchaseInvoiceAsync(totalCost: 100m);

        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);

        var handler = scope.ServiceProvider.GetRequiredService<RecordPurchaseInvoicePaymentHandler>();
        var clientRequestId = Guid.NewGuid();

        var first = await handler.HandleAsync(
            new RecordPurchaseInvoicePaymentCommand(invoiceId, TestDataBuilder.CashPaymentMethodId, 30m, null, clientRequestId),
            CancellationToken.None);
        Assert.True(first.IsSuccess);

        var second = await handler.HandleAsync(
            new RecordPurchaseInvoicePaymentCommand(invoiceId, TestDataBuilder.CashPaymentMethodId, 30m, null, clientRequestId),
            CancellationToken.None);

        Assert.True(second.IsFailure);
        Assert.Equal(ErrorType.Conflict, second.Error!.Type);
        Assert.Equal("Payment.DuplicateRequest", second.Error.Code);

        var paymentCount = await db.PurchaseInvoicePayments.CountAsync(p => p.ClientRequestId == clientRequestId);
        Assert.Equal(1, paymentCount);
    }

    [Fact]
    public async Task دين_المورد_ينعكس_بشاشة_الديون_وينخفض_بعد_الدفع()
    {
        var invoiceId = await CreateAPurchaseInvoiceAsync(totalCost: 100m);

        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);

        var debtsHandler = scope.ServiceProvider.GetRequiredService<GetSupplierDebtsHandler>();
        var beforePayment = await debtsHandler.HandleAsync(CancellationToken.None);
        Assert.Single(beforePayment.Suppliers);
        Assert.Equal(100m, beforePayment.Suppliers[0].RemainingDebt);

        var paymentHandler = scope.ServiceProvider.GetRequiredService<RecordPurchaseInvoicePaymentHandler>();
        var payment = await paymentHandler.HandleAsync(
            new RecordPurchaseInvoicePaymentCommand(invoiceId, TestDataBuilder.CashPaymentMethodId, 100m, null, Guid.NewGuid()),
            CancellationToken.None);
        Assert.True(payment.IsSuccess);

        var afterPayment = await debtsHandler.HandleAsync(CancellationToken.None);
        // انسدّد الدين بالكامل - المورد ما عاد يظهر إطلاقًا (RemainingDebt > 0 فقط).
        Assert.Empty(afterPayment.Suppliers);
    }
}
