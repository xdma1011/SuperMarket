using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Application.Sales.CompleteSale;
using SupermarketSystem.Domain.Sales;
using SupermarketSystem.Infrastructure.Persistence;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Sales;

/// <summary>
/// CompleteSaleHandler — أهم عملية بالنظام. التركيز هون على القواعد
/// الحاكمة الموثَّقة بتعليق الـHandler نفسه: السعر دايمًا من السيرفر
/// (CLAUDE.md §3.6)، خصم المخزون الذري، وIdempotency (§3.2).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class CompleteSaleTests : IntegrationTestBase
{
    public CompleteSaleTests(DatabaseFixture fixture) : base(fixture) { }

    private async Task<(Guid ProductId, Guid UnitId)> SeedSellableProductAsync(AppDbContext db, decimal price = 10m, decimal stock = 100m)
    {
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج للبيع");
        await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, price);
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, stock);
        return (product.Id, unit.Id);
    }

    [Fact]
    public async Task إتمام_بيع_بسيط_ينجح_ويحسب_الإجمالي_من_سعر_السيرفر()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (productId, unitId) = await SeedSellableProductAsync(db, price: 10m, stock: 50m);

        var handler = scope.ServiceProvider.GetRequiredService<CompleteSaleHandler>();
        var command = new CompleteSaleCommand(
            Fixture.TestBranchId,
            Guid.NewGuid(),
            CustomerId: null,
            InvoiceLevelDiscountAmount: 0m,
            Items: new[] { new CompleteSaleItemDto(productId, unitId, Quantity: 3m, ManualDiscountAmount: 0m, ProductBatchId: null) },
            Payments: new[] { new CompleteSalePaymentDto(TestDataBuilder.CashPaymentMethodId, 30m, null, Guid.NewGuid()) });

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.WasReplay);
        Assert.Equal(30m, result.Value.TotalAmount);
        Assert.Equal(30m, result.Value.TotalPaidAmount);
        Assert.Empty(result.Value.ReviewFlags);

        var stockAfter = await db.Stocks.AsNoTracking().FirstAsync(s => s.ProductId == productId);
        Assert.Equal(47m, stockAfter.QuantityOnHand);
    }

    [Fact]
    public async Task السعر_يُحسب_من_ProductBranch_دائمًا_ولا_يوجد_حقل_لإرسال_سعر_من_العميل()
    {
        // CompleteSaleItemDto ما بيقبل UnitPrice إطلاقًا (راجع CLAUDE.md
        // §3.6) - هذا الاختبار يثبّت ذلك فعليًا: نبيع بسعر ProductBranch =
        // 10، ونتأكد الإجمالي المحسوب هو 10 بالضبط بغض النظر عن أي محاولة.
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (productId, unitId) = await SeedSellableProductAsync(db, price: 10m, stock: 10m);

        var handler = scope.ServiceProvider.GetRequiredService<CompleteSaleHandler>();
        var command = new CompleteSaleCommand(
            Fixture.TestBranchId, Guid.NewGuid(), null, 0m,
            new[] { new CompleteSaleItemDto(productId, unitId, 1m, 0m, null) },
            new[] { new CompleteSalePaymentDto(TestDataBuilder.CashPaymentMethodId, 10m, null, Guid.NewGuid()) });

        var result = await handler.HandleAsync(command, CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal(10m, result.Value.TotalAmount);

        // لو حاولنا ندفع بمبلغ مختلف (كأننا حاولنا نفرض سعر مختلف)، الفشل
        // بيكون "المدفوعات ما تسوي الإجمالي" - دليل إضافي إن السعر المعتمَد
        // هو سعر السيرفر (10) لا أي قيمة يقترحها الطالب.
        var mismatchedCommand = command with
        {
            ClientRequestId = Guid.NewGuid(),
            Payments = new[] { new CompleteSalePaymentDto(TestDataBuilder.CashPaymentMethodId, 5m, null, Guid.NewGuid()) }
        };
        var mismatchedResult = await handler.HandleAsync(mismatchedCommand, CancellationToken.None);
        Assert.True(mismatchedResult.IsFailure);
        Assert.Equal("Sale.PaymentsDoNotSettleTotal", mismatchedResult.Error!.Code);
    }

    [Fact]
    public async Task بيع_كمية_أكبر_من_المخزون_مع_منع_الرصيد_السالب_يفشل_بخطأ_واضح()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (productId, unitId) = await SeedSellableProductAsync(db, price: 10m, stock: 5m);

        // نمنع صراحة الرصيد السالب - الافتراضي بالنظام (true) بيسمح بالبيع
        // فيعلّمه للمراجعة بدل ما يرفضه (راجع الاختبار التالي)، فهذا الاختبار
        // بالذات يحتاج يعطّل الإعداد صراحة ليشوف مسار الرفض.
        await TestDataBuilder.SetSettingAsync(scope, InventorySettingsKeys.AllowNegativeStock, value: false);

        var handler = scope.ServiceProvider.GetRequiredService<CompleteSaleHandler>();
        var command = new CompleteSaleCommand(
            Fixture.TestBranchId, Guid.NewGuid(), null, 0m,
            new[] { new CompleteSaleItemDto(productId, unitId, 10m, 0m, null) },
            new[] { new CompleteSalePaymentDto(TestDataBuilder.CashPaymentMethodId, 100m, null, Guid.NewGuid()) });

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.BusinessRule, result.Error!.Type);
        Assert.Equal("Sale.InsufficientStock", result.Error.Code);

        // المخزون ما لازم يتأثر إطلاقًا - العملية فشلت بالكامل (ذرية).
        var stockAfter = await db.Stocks.AsNoTracking().FirstAsync(s => s.ProductId == productId);
        Assert.Equal(5m, stockAfter.QuantityOnHand);
    }

    [Fact]
    public async Task بيع_كمية_أكبر_من_المخزون_بالإعداد_الافتراضي_ينجح_وينزل_الرصيد_تحت_الصفر_ويُعلَّم_للمراجعة()
    {
        // الإعداد الافتراضي AllowNegativeStock=true - البيع ما يتوقف أبدًا
        // لمجرد نقص مخزون بالنظام (نفس فلسفة "سماح مع مراجعة" §1.6 بمعناها
        // الأوسع الموثَّق بالـHandler نفسه).
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (productId, unitId) = await SeedSellableProductAsync(db, price: 10m, stock: 2m);

        var handler = scope.ServiceProvider.GetRequiredService<CompleteSaleHandler>();
        var command = new CompleteSaleCommand(
            Fixture.TestBranchId, Guid.NewGuid(), null, 0m,
            new[] { new CompleteSaleItemDto(productId, unitId, 5m, 0m, null) },
            new[] { new CompleteSalePaymentDto(TestDataBuilder.CashPaymentMethodId, 50m, null, Guid.NewGuid()) });

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.ReviewFlags);

        var stockAfter = await db.Stocks.AsNoTracking().FirstAsync(s => s.ProductId == productId);
        Assert.Equal(-3m, stockAfter.QuantityOnHand);
    }

    [Fact]
    public async Task إرسال_نفس_ClientRequestId_مرتين_يرجّع_نفس_البيع_الأصلي_بلا_تكرار()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (productId, unitId) = await SeedSellableProductAsync(db, price: 10m, stock: 100m);

        var handler = scope.ServiceProvider.GetRequiredService<CompleteSaleHandler>();
        var clientRequestId = Guid.NewGuid();
        var command = new CompleteSaleCommand(
            Fixture.TestBranchId, clientRequestId, null, 0m,
            new[] { new CompleteSaleItemDto(productId, unitId, 2m, 0m, null) },
            new[] { new CompleteSalePaymentDto(TestDataBuilder.CashPaymentMethodId, 20m, null, Guid.NewGuid()) });

        var first = await handler.HandleAsync(command, CancellationToken.None);
        Assert.True(first.IsSuccess);
        Assert.False(first.Value.WasReplay);

        // نفس الطلب بالضبط، ثاني مرة - محاكاة إعادة إرسال بعد انقطاع شبكة.
        var second = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(second.IsSuccess);
        Assert.True(second.Value.WasReplay);
        Assert.Equal(first.Value.SaleInvoiceId, second.Value.SaleInvoiceId);
        Assert.Equal(first.Value.InvoiceNumber, second.Value.InvoiceNumber);

        var invoiceCount = await db.SaleInvoices.CountAsync(s => s.ClientRequestId == clientRequestId);
        Assert.Equal(1, invoiceCount);

        // المخزون انخصم مرة وحدة بس، لا مرتين.
        var stockAfter = await db.Stocks.AsNoTracking().FirstAsync(s => s.ProductId == productId);
        Assert.Equal(98m, stockAfter.QuantityOnHand);
    }

    [Fact]
    public async Task بيع_بلا_أصناف_يفشل_بخطأ_تحقق()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var handler = scope.ServiceProvider.GetRequiredService<CompleteSaleHandler>();

        var command = new CompleteSaleCommand(
            Fixture.TestBranchId, Guid.NewGuid(), null, 0m,
            Items: Array.Empty<CompleteSaleItemDto>(),
            Payments: Array.Empty<CompleteSalePaymentDto>());

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Equal("Sale.ItemsRequired", result.Error.Code);
    }

    [Fact]
    public async Task بيع_منتج_غير_مسعّر_بالفرع_يفشل_بقاعدة_عمل_واضحة()
    {
        // منتج فعّال بس بلا صف ProductBranch إطلاقًا - يحاكي بالضبط فجوة
        // §1.8 بـCLAUDE.md (منتج بالكتالوج بلا ربط بفرع+سعر).
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج بلا سعر بالفرع");

        var handler = scope.ServiceProvider.GetRequiredService<CompleteSaleHandler>();
        var command = new CompleteSaleCommand(
            Fixture.TestBranchId, Guid.NewGuid(), null, 0m,
            new[] { new CompleteSaleItemDto(product.Id, unit.Id, 1m, 0m, null) },
            new[] { new CompleteSalePaymentDto(TestDataBuilder.CashPaymentMethodId, 1m, null, Guid.NewGuid()) });

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.BusinessRule, result.Error!.Type);
        Assert.Equal("Sale.ProductNotPricedAtBranch", result.Error.Code);
    }

    [Fact]
    public async Task خصم_يدوي_يتجاوز_النسبة_المسموحة_يُرفض()
    {
        // الافتراضي: Pos.MaxManualDiscountPercentage = 10% (راجع PosPolicyService).
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (productId, unitId) = await SeedSellableProductAsync(db, price: 100m, stock: 10m);

        var handler = scope.ServiceProvider.GetRequiredService<CompleteSaleHandler>();
        var command = new CompleteSaleCommand(
            Fixture.TestBranchId, Guid.NewGuid(), null, 0m,
            // خصم 50 على سطر قيمته 100 = 50% - أعلى بكثير من حد 10% الافتراضي.
            new[] { new CompleteSaleItemDto(productId, unitId, 1m, ManualDiscountAmount: 50m, ProductBatchId: null) },
            new[] { new CompleteSalePaymentDto(TestDataBuilder.CashPaymentMethodId, 50m, null, Guid.NewGuid()) });

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Forbidden, result.Error!.Type);
        Assert.Equal("Sale.ManualDiscountDenied", result.Error.Code);
    }
}
