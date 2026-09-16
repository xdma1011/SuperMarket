using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Catalog.CreatePromotion;
using SupermarketSystem.Application.Catalog.GetProductPromotions;
using SupermarketSystem.Application.Catalog.GetPublicCatalog;
using SupermarketSystem.Application.Catalog.SetPromotionBranchActive;
using SupermarketSystem.Application.Catalog.UpdatePromotion;
using SupermarketSystem.Application.CashierSync.GetCatalogSyncPage;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Application.Sales.CompleteSale;
using SupermarketSystem.IntegrationTests.Common;
using SupermarketSystem.IntegrationTests;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Catalog;

/// <summary>
/// عرض الكمية (Bundle Promotion) - "N بسعر كذا" - طلب صاحب المشروع
/// المباشر (16/9/2026)، ليس ملاحظة مؤجَّلة. يغطي: الحساب التلقائي وقت
/// البيع (لا خصم يدوي)، الحد الأعلى بالفاتورة، فترة الصلاحية، التفعيل
/// المستقل لكل فرع، وثبات الفاتورة القديمة بعد انتهاء/تعديل العرض.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PromotionTests : IntegrationTestBase
{
    public PromotionTests(DatabaseFixture fixture) : base(fixture) { }

    private static DateTime ActiveWindowStart => DateTime.UtcNow.AddDays(-1);
    private static DateTime ActiveWindowEnd => DateTime.UtcNow.AddDays(30);

    [Fact]
    public async Task إنشاء_عرض_بلا_تحديد_فروع_يُربط_تلقائيًا_بكل_الفروع_الفعّالة()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, _) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج عرض عام");

        var handler = scope.ServiceProvider.GetRequiredService<CreatePromotionHandler>();
        var result = await handler.HandleAsync(
            new CreatePromotionCommand(product.Id, "3 بدينار", 3, 1m, null, ActiveWindowStart, ActiveWindowEnd, BranchIds: null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(Fixture.TestBranchId, result.Value.BranchIds);

        var getHandler = scope.ServiceProvider.GetRequiredService<GetProductPromotionsHandler>();
        var promotions = await getHandler.HandleAsync(new GetProductPromotionsQuery(product.Id), CancellationToken.None);
        var promo = Assert.Single(promotions);
        Assert.True(promo.Branches.Single(b => b.BranchId == Fixture.TestBranchId).IsActive);
    }

    [Fact]
    public async Task بيع_كمية_تساوي_مضاعف_الحزمة_بالضبط_يُحسب_بسعر_العرض_كاملًا()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج 3 بدينار");
        await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, sellingPrice: 1m);
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 100m);

        var createHandler = scope.ServiceProvider.GetRequiredService<CreatePromotionHandler>();
        await createHandler.HandleAsync(
            new CreatePromotionCommand(product.Id, "3 بدينار", 3, 1m, null, ActiveWindowStart, ActiveWindowEnd, new[] { Fixture.TestBranchId }),
            CancellationToken.None);

        var saleHandler = scope.ServiceProvider.GetRequiredService<CompleteSaleHandler>();
        var result = await saleHandler.HandleAsync(new CompleteSaleCommand(
            Fixture.TestBranchId, Guid.NewGuid(), CustomerId: null, InvoiceLevelDiscountAmount: 0m,
            Items: new[] { new CompleteSaleItemDto(product.Id, unit.Id, Quantity: 3m, ManualDiscountAmount: 0m, ProductBatchId: null) },
            Payments: new[] { new CompleteSalePaymentDto(TestDataBuilder.CashPaymentMethodId, 1m, null, Guid.NewGuid()) }),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1m, result.Value.TotalAmount); // بلا العرض كان لازم يكون 3 دنانير

        var item = await db.SaleInvoiceItems.AsNoTracking().FirstAsync(i => i.SaleInvoiceId == result.Value.SaleInvoiceId);
        Assert.Equal(2m, item.PromotionAmount); // (3 × 1) - 1 = 2 دينار توفير
        Assert.Equal("3 بدينار", item.PromotionTitleSnapshot);
        Assert.NotNull(item.PromotionId);
    }

    [Fact]
    public async Task بيع_كمية_أكبر_من_الحزمة_بواحدة_يحسب_حبة_زيادة_بالسعر_العادي()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج 3 بدينار زيادة");
        await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, sellingPrice: 1m);
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 100m);

        var createHandler = scope.ServiceProvider.GetRequiredService<CreatePromotionHandler>();
        await createHandler.HandleAsync(
            new CreatePromotionCommand(product.Id, "3 بدينار", 3, 1m, null, ActiveWindowStart, ActiveWindowEnd, new[] { Fixture.TestBranchId }),
            CancellationToken.None);

        var saleHandler = scope.ServiceProvider.GetRequiredService<CompleteSaleHandler>();
        var result = await saleHandler.HandleAsync(new CompleteSaleCommand(
            Fixture.TestBranchId, Guid.NewGuid(), CustomerId: null, InvoiceLevelDiscountAmount: 0m,
            Items: new[] { new CompleteSaleItemDto(product.Id, unit.Id, Quantity: 4m, ManualDiscountAmount: 0m, ProductBatchId: null) },
            Payments: new[] { new CompleteSalePaymentDto(TestDataBuilder.CashPaymentMethodId, 2m, null, Guid.NewGuid()) }),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2m, result.Value.TotalAmount); // 1 (حزمة 3) + 1 (حبة عادية) = 2
    }

    [Fact]
    public async Task شراء_حبة_وحدة_اقل_من_الحزمة_يضل_بالسعر_العادي_بلا_عرض()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج 3 بدينار حبة وحدة");
        await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, sellingPrice: 1m);
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 100m);

        var createHandler = scope.ServiceProvider.GetRequiredService<CreatePromotionHandler>();
        await createHandler.HandleAsync(
            new CreatePromotionCommand(product.Id, "3 بدينار", 3, 1m, null, ActiveWindowStart, ActiveWindowEnd, new[] { Fixture.TestBranchId }),
            CancellationToken.None);

        var saleHandler = scope.ServiceProvider.GetRequiredService<CompleteSaleHandler>();
        var result = await saleHandler.HandleAsync(new CompleteSaleCommand(
            Fixture.TestBranchId, Guid.NewGuid(), CustomerId: null, InvoiceLevelDiscountAmount: 0m,
            Items: new[] { new CompleteSaleItemDto(product.Id, unit.Id, Quantity: 1m, ManualDiscountAmount: 0m, ProductBatchId: null) },
            Payments: new[] { new CompleteSalePaymentDto(TestDataBuilder.CashPaymentMethodId, 1m, null, Guid.NewGuid()) }),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1m, result.Value.TotalAmount);

        var item = await db.SaleInvoiceItems.AsNoTracking().FirstAsync(i => i.SaleInvoiceId == result.Value.SaleInvoiceId);
        Assert.Equal(0m, item.PromotionAmount);
        Assert.Null(item.PromotionId);
    }

    [Fact]
    public async Task تجاوز_الحد_الأعلى_للفاتورة_يسعّر_الزيادة_عاديًا()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج بحد أعلى");
        await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, sellingPrice: 1m);
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 100m);

        // حد أعلى 3 حبات بسعر العرض بكل فاتورة - يعني حزمة وحدة كحد أقصى.
        var createHandler = scope.ServiceProvider.GetRequiredService<CreatePromotionHandler>();
        await createHandler.HandleAsync(
            new CreatePromotionCommand(product.Id, "3 بدينار (حد 3)", 3, 1m, MaxQuantityPerInvoice: 3m, ActiveWindowStart, ActiveWindowEnd, new[] { Fixture.TestBranchId }),
            CancellationToken.None);

        var saleHandler = scope.ServiceProvider.GetRequiredService<CompleteSaleHandler>();
        // يشتري 6 (حزمتين لو ما كان في حد) - بالحد الأعلى 3، بس 3 بسعر
        // العرض (دينار)، والباقي (3) بالسعر العادي (3 دنانير) = 4 دنانير.
        var result = await saleHandler.HandleAsync(new CompleteSaleCommand(
            Fixture.TestBranchId, Guid.NewGuid(), CustomerId: null, InvoiceLevelDiscountAmount: 0m,
            Items: new[] { new CompleteSaleItemDto(product.Id, unit.Id, Quantity: 6m, ManualDiscountAmount: 0m, ProductBatchId: null) },
            Payments: new[] { new CompleteSalePaymentDto(TestDataBuilder.CashPaymentMethodId, 4m, null, Guid.NewGuid()) }),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(4m, result.Value.TotalAmount);
    }

    [Fact]
    public async Task عرض_خارج_فترة_صلاحيته_ما_ينطبق_إطلاقًا()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج عرض منتهي");
        await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, sellingPrice: 1m);
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 100m);

        var createHandler = scope.ServiceProvider.GetRequiredService<CreatePromotionHandler>();
        await createHandler.HandleAsync(
            new CreatePromotionCommand(
                product.Id, "عرض منتهي", 3, 1m, null,
                DateTime.UtcNow.AddDays(-30), DateTime.UtcNow.AddDays(-1), new[] { Fixture.TestBranchId }),
            CancellationToken.None);

        var saleHandler = scope.ServiceProvider.GetRequiredService<CompleteSaleHandler>();
        var result = await saleHandler.HandleAsync(new CompleteSaleCommand(
            Fixture.TestBranchId, Guid.NewGuid(), CustomerId: null, InvoiceLevelDiscountAmount: 0m,
            Items: new[] { new CompleteSaleItemDto(product.Id, unit.Id, Quantity: 3m, ManualDiscountAmount: 0m, ProductBatchId: null) },
            Payments: new[] { new CompleteSalePaymentDto(TestDataBuilder.CashPaymentMethodId, 3m, null, Guid.NewGuid()) }),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3m, result.Value.TotalAmount); // بلا عرض - 3 حبات بسعرها العادي
    }

    [Fact]
    public async Task توقيف_العرض_بفرع_واحد_يخلّيه_شغّال_بالفرع_التاني_بلا_تأثّر()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var otherBranch = await TestDataBuilder.CreateBranchAsync(db, "فرع تاني للعرض", "PROMO2");
        await BranchDocumentSequenceHelper.EnsureProvisionedAsync(Fixture.Factory.Services, otherBranch.Id);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج فرعين");
        await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, sellingPrice: 1m);
        await TestDataBuilder.CreateProductBranchAsync(db, product.Id, otherBranch.Id, sellingPrice: 1m);
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 100m);
        await TestDataBuilder.SetStockAsync(db, product.Id, otherBranch.Id, 100m);

        var createHandler = scope.ServiceProvider.GetRequiredService<CreatePromotionHandler>();
        var createResult = await createHandler.HandleAsync(
            new CreatePromotionCommand(product.Id, "3 بدينار فرعين", 3, 1m, null, ActiveWindowStart, ActiveWindowEnd,
                new[] { Fixture.TestBranchId, otherBranch.Id }),
            CancellationToken.None);

        var toggleHandler = scope.ServiceProvider.GetRequiredService<SetPromotionBranchActiveHandler>();
        var toggleResult = await toggleHandler.HandleAsync(
            new SetPromotionBranchActiveCommand(createResult.Value.PromotionId, Fixture.TestBranchId, IsActive: false),
            CancellationToken.None);
        Assert.True(toggleResult.IsSuccess);

        var saleHandler = scope.ServiceProvider.GetRequiredService<CompleteSaleHandler>();

        // فرع الاختبار: العرض موقوف - بيع 3 حبات = 3 دنانير عادي.
        var resultTestBranch = await saleHandler.HandleAsync(new CompleteSaleCommand(
            Fixture.TestBranchId, Guid.NewGuid(), CustomerId: null, InvoiceLevelDiscountAmount: 0m,
            Items: new[] { new CompleteSaleItemDto(product.Id, unit.Id, Quantity: 3m, ManualDiscountAmount: 0m, ProductBatchId: null) },
            Payments: new[] { new CompleteSalePaymentDto(TestDataBuilder.CashPaymentMethodId, 3m, null, Guid.NewGuid()) }),
            CancellationToken.None);
        Assert.True(resultTestBranch.IsSuccess);
        Assert.Equal(3m, resultTestBranch.Value.TotalAmount);

        // الفرع التاني: العرض لسه شغّال - بيع 3 حبات = دينار وحدة.
        var resultOtherBranch = await saleHandler.HandleAsync(new CompleteSaleCommand(
            otherBranch.Id, Guid.NewGuid(), CustomerId: null, InvoiceLevelDiscountAmount: 0m,
            Items: new[] { new CompleteSaleItemDto(product.Id, unit.Id, Quantity: 3m, ManualDiscountAmount: 0m, ProductBatchId: null) },
            Payments: new[] { new CompleteSalePaymentDto(TestDataBuilder.CashPaymentMethodId, 1m, null, Guid.NewGuid()) }),
            CancellationToken.None);
        Assert.True(resultOtherBranch.IsSuccess);
        Assert.Equal(1m, resultOtherBranch.Value.TotalAmount);
    }

    [Fact]
    public async Task تعديل_تفاصيل_العرض_لاحقًا_ما_يغيّر_فاتورة_قديمة_بيعت_تحت_الشروط_الأصلية()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج تعديل عرض");
        await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, sellingPrice: 1m);
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 100m);

        var createHandler = scope.ServiceProvider.GetRequiredService<CreatePromotionHandler>();
        var createResult = await createHandler.HandleAsync(
            new CreatePromotionCommand(product.Id, "3 بدينار أصلي", 3, 1m, null, ActiveWindowStart, ActiveWindowEnd, new[] { Fixture.TestBranchId }),
            CancellationToken.None);

        var saleHandler = scope.ServiceProvider.GetRequiredService<CompleteSaleHandler>();
        var saleResult = await saleHandler.HandleAsync(new CompleteSaleCommand(
            Fixture.TestBranchId, Guid.NewGuid(), CustomerId: null, InvoiceLevelDiscountAmount: 0m,
            Items: new[] { new CompleteSaleItemDto(product.Id, unit.Id, Quantity: 3m, ManualDiscountAmount: 0m, ProductBatchId: null) },
            Payments: new[] { new CompleteSalePaymentDto(TestDataBuilder.CashPaymentMethodId, 1m, null, Guid.NewGuid()) }),
            CancellationToken.None);
        Assert.True(saleResult.IsSuccess);

        // العرض يتعدَّل لاحقًا لعنوان وسعر مختلفين كليًا.
        var updateHandler = scope.ServiceProvider.GetRequiredService<UpdatePromotionHandler>();
        var updateResult = await updateHandler.HandleAsync(
            new UpdatePromotionCommand(createResult.Value.PromotionId, "5 بدينارين", 5, 2m, null, ActiveWindowStart, ActiveWindowEnd),
            CancellationToken.None);
        Assert.True(updateResult.IsSuccess);

        // الفاتورة القديمة تحتفظ بالعنوان والمبلغ الأصليين تمامًا - Snapshot ثابت.
        var item = await db.SaleInvoiceItems.AsNoTracking().FirstAsync(i => i.SaleInvoiceId == saleResult.Value.SaleInvoiceId);
        Assert.Equal("3 بدينار أصلي", item.PromotionTitleSnapshot);
        Assert.Equal(2m, item.PromotionAmount);
        Assert.Equal(1m, item.LineTotal);
    }

    [Fact]
    public async Task كتالوج_الزبون_العام_يظهر_تفاصيل_العرض_الشغّال_حاليًا()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, _) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج كتالوج زبون بعرض");
        await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, sellingPrice: 1m);
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 100m);

        var createHandler = scope.ServiceProvider.GetRequiredService<CreatePromotionHandler>();
        await createHandler.HandleAsync(
            new CreatePromotionCommand(product.Id, "3 بدينار للزبون", 3, 1m, null, ActiveWindowStart, ActiveWindowEnd, new[] { Fixture.TestBranchId }),
            CancellationToken.None);

        var catalogHandler = scope.ServiceProvider.GetRequiredService<GetPublicCatalogHandler>();
        var catalog = await catalogHandler.HandleAsync(
            new GetPublicCatalogQuery(Fixture.TestBranchId, CategoryId: null, new SupermarketSystem.Application.Common.Pagination.PagedRequest()),
            CancellationToken.None);

        var item = Assert.Single(catalog.Items, i => i.ProductId == product.Id);
        Assert.Equal("3 بدينار للزبون", item.ActivePromotionTitle);
        Assert.Equal(3, item.ActivePromotionBundleQuantity);
        Assert.Equal(1m, item.ActivePromotionBundlePrice);
    }

    [Fact]
    public async Task مزامنة_كتالوج_الكاشير_تُظهر_العرض_الشغّال_حاليًا_للتقدير_المحلي()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, _) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج كاشير بعرض");
        await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, sellingPrice: 1m);

        var createHandler = scope.ServiceProvider.GetRequiredService<CreatePromotionHandler>();
        await createHandler.HandleAsync(
            new CreatePromotionCommand(product.Id, "3 بدينار كاشير", 3, 1m, 6m, ActiveWindowStart, ActiveWindowEnd, new[] { Fixture.TestBranchId }),
            CancellationToken.None);

        var syncHandler = scope.ServiceProvider.GetRequiredService<GetCatalogSyncPageHandler>();
        var page = await syncHandler.HandleAsync(new GetCatalogSyncPageQuery(Fixture.TestBranchId, 1, 200), CancellationToken.None);

        var item = Assert.Single(page.Items, i => i.ProductId == product.Id);
        Assert.NotNull(item.ActivePromotion);
        Assert.Equal("3 بدينار كاشير", item.ActivePromotion!.Title);
        Assert.Equal(3, item.ActivePromotion.BundleQuantity);
        Assert.Equal(1m, item.ActivePromotion.BundlePrice);
        Assert.Equal(6m, item.ActivePromotion.MaxQuantityPerInvoice);
    }
}
