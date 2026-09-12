using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Application.Purchasing.PurchaseInvoiceDrafts;
using SupermarketSystem.Domain.Purchasing;
using SupermarketSystem.Infrastructure.Persistence;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Purchasing;

/// <summary>
/// دورة حياة مسودة فاتورة شراء (Discard/Update/Complete/Get) — تُبذَر
/// مباشرة بقاعدة البيانات (بمنشئ PurchaseInvoiceDraft العام) بدل استدعاء
/// CreatePurchaseInvoiceDraftFromImageHandler، لأن ذاك يعتمد فعليًا على
/// IInvoiceExtractionService (خدمة ذكاء اصطناعي حقيقية خارجية بلا بديل
/// اختباري مسجَّل بالـDI) - راجع ملاحظة "Handler بلا اختبار" بالتقرير
/// النهائي.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PurchaseInvoiceDraftTests : IntegrationTestBase
{
    public PurchaseInvoiceDraftTests(DatabaseFixture fixture) : base(fixture) { }

    private static PurchaseInvoiceDraft NewDraft(Guid branchId, IReadOnlyList<PurchaseInvoiceDraftItemDto> items) => new(
        branchId,
        imageReference: "test/image.webp",
        providerName: "Test",
        rawSupplierName: "مورد كما استخرجه الذكاء الاصطناعي",
        matchedSupplierId: null,
        supplierInvoiceReference: null,
        invoiceDate: null,
        currency: "JOD",
        extractedInvoiceTotal: null,
        extractionConfidence: "High",
        warningsText: PurchaseInvoiceDraftItemsSerializer.SerializeWarnings(Array.Empty<string>()),
        itemsJson: PurchaseInvoiceDraftItemsSerializer.Serialize(items),
        paidNowAmount: null,
        paidNowPaymentMethodId: null);

    [Fact]
    public async Task جلب_مسودة_بمعرّفها_يرجّع_تفاصيلها_كما_بُذرت()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var draft = NewDraft(Fixture.TestBranchId, new[]
        {
            new PurchaseInvoiceDraftItemDto("سكر 5 كيلو", 2m, "كيس", 3m, 6m, null, null, null, false, null, null)
        });
        db.PurchaseInvoiceDrafts.Add(draft);
        await db.SaveChangesAsync();

        var handler = scope.ServiceProvider.GetRequiredService<GetPurchaseInvoiceDraftByIdHandler>();
        var result = await handler.HandleAsync(new GetPurchaseInvoiceDraftByIdQuery(draft.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal((int)PurchaseInvoiceDraftStatus.PendingReview, result.Value.Status);
        Assert.Single(result.Value.Items);
    }

    [Fact]
    public async Task قائمة_المسودات_تعرض_افتراضيًا_بانتظار_المراجعة_فقط()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var pendingDraft = NewDraft(Fixture.TestBranchId, Array.Empty<PurchaseInvoiceDraftItemDto>());
        var discardedDraft = NewDraft(Fixture.TestBranchId, Array.Empty<PurchaseInvoiceDraftItemDto>());
        discardedDraft.Discard();
        db.PurchaseInvoiceDrafts.AddRange(pendingDraft, discardedDraft);
        await db.SaveChangesAsync();

        var handler = scope.ServiceProvider.GetRequiredService<GetPurchaseInvoiceDraftsHandler>();
        var result = await handler.HandleAsync(
            new GetPurchaseInvoiceDraftsQuery(new PagedRequest(), Fixture.TestBranchId, Status: null), CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(pendingDraft.Id, item.Id);
    }

    [Fact]
    public async Task تعديل_مسودة_يطابق_السطر_بمنتج_فعلي_ويحدّث_المورد()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج مطابقة المسودة");
        var supplier = await TestDataBuilder.CreateSupplierAsync(db);

        var draft = NewDraft(Fixture.TestBranchId, new[]
        {
            new PurchaseInvoiceDraftItemDto("اسم مستخرَج غير دقيق", 1m, "حبة", 2m, 2m, null, null, null, false, null, null)
        });
        db.PurchaseInvoiceDrafts.Add(draft);
        await db.SaveChangesAsync();

        var updateHandler = scope.ServiceProvider.GetRequiredService<UpdatePurchaseInvoiceDraftHandler>();
        var updatedItems = new[]
        {
            new PurchaseInvoiceDraftItemDto("اسم مستخرَج غير دقيق", 1m, "حبة", 2m, 2m, product.Id, product.Name, unit.Id, false, null, null)
        };
        var result = await updateHandler.HandleAsync(
            new UpdatePurchaseInvoiceDraftCommand(draft.Id, supplier.Id, "REF-DRAFT-1", updatedItems), CancellationToken.None);

        Assert.True(result.IsSuccess);

        var updatedDraft = await db.PurchaseInvoiceDrafts.AsNoTracking().FirstAsync(d => d.Id == draft.Id);
        Assert.Equal(supplier.Id, updatedDraft.MatchedSupplierId);
        Assert.Equal("REF-DRAFT-1", updatedDraft.SupplierInvoiceReference);
    }

    [Fact]
    public async Task اعتماد_مسودة_فيها_سطر_غير_مطابَق_يُرفض()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var supplier = await TestDataBuilder.CreateSupplierAsync(db);
        var draft = NewDraft(Fixture.TestBranchId, new[]
        {
            new PurchaseInvoiceDraftItemDto("صنف غير مطابَق", 1m, "حبة", 2m, 2m, null, null, null, false, null, null)
        });
        db.PurchaseInvoiceDrafts.Add(draft);
        await db.SaveChangesAsync();

        var updateHandler = scope.ServiceProvider.GetRequiredService<UpdatePurchaseInvoiceDraftHandler>();
        await updateHandler.HandleAsync(
            new UpdatePurchaseInvoiceDraftCommand(draft.Id, supplier.Id, null, PurchaseInvoiceDraftItemsSerializer.Deserialize(draft.ItemsJson)),
            CancellationToken.None);

        var completeHandler = scope.ServiceProvider.GetRequiredService<CompletePurchaseInvoiceDraftHandler>();
        var result = await completeHandler.HandleAsync(new CompletePurchaseInvoiceDraftCommand(draft.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Equal("PurchaseInvoiceDraft.ItemNotMatched", result.Error.Code);
    }

    [Fact]
    public async Task اعتماد_مسودة_مطابَقة_بالكامل_يحوّلها_لفاتورة_شراء_حقيقية_وتزيد_المخزون()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج اعتماد مسودة");
        var supplier = await TestDataBuilder.CreateSupplierAsync(db);

        var draft = NewDraft(Fixture.TestBranchId, new[]
        {
            new PurchaseInvoiceDraftItemDto("اسم مستخرَج", 4m, "حبة", 3m, 12m, product.Id, product.Name, unit.Id, false, null, null)
        });
        draft.UpdateForReview(supplier.Id, "REF-DRAFT-2", draft.ItemsJson);
        db.PurchaseInvoiceDrafts.Add(draft);
        await db.SaveChangesAsync();

        var completeHandler = scope.ServiceProvider.GetRequiredService<CompletePurchaseInvoiceDraftHandler>();
        var result = await completeHandler.HandleAsync(new CompletePurchaseInvoiceDraftCommand(draft.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(12m, result.Value.TotalAmount);

        var stock = await db.Stocks.AsNoTracking().FirstAsync(s => s.ProductId == product.Id);
        Assert.Equal(4m, stock.QuantityOnHand);

        var updatedDraft = await db.PurchaseInvoiceDrafts.AsNoTracking().FirstAsync(d => d.Id == draft.Id);
        Assert.Equal(PurchaseInvoiceDraftStatus.Completed, updatedDraft.Status);
        Assert.Equal(result.Value.PurchaseInvoiceId, updatedDraft.ResultingPurchaseInvoiceId);
    }

    [Fact]
    public async Task تجاهل_مسودة_بانتظار_المراجعة_ينجح()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var draft = NewDraft(Fixture.TestBranchId, Array.Empty<PurchaseInvoiceDraftItemDto>());
        db.PurchaseInvoiceDrafts.Add(draft);
        await db.SaveChangesAsync();

        var handler = scope.ServiceProvider.GetRequiredService<DiscardPurchaseInvoiceDraftHandler>();
        var result = await handler.HandleAsync(new DiscardPurchaseInvoiceDraftCommand(draft.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);

        var updated = await db.PurchaseInvoiceDrafts.AsNoTracking().FirstAsync(d => d.Id == draft.Id);
        Assert.Equal(PurchaseInvoiceDraftStatus.Discarded, updated.Status);
    }

    [Fact]
    public async Task تجاهل_مسودة_مُعتمَدة_مسبقًا_يفشل_بقاعدة_عمل_واضحة()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var draft = NewDraft(Fixture.TestBranchId, Array.Empty<PurchaseInvoiceDraftItemDto>());
        db.PurchaseInvoiceDrafts.Add(draft);
        await db.SaveChangesAsync();

        var discardHandler = scope.ServiceProvider.GetRequiredService<DiscardPurchaseInvoiceDraftHandler>();
        var first = await discardHandler.HandleAsync(new DiscardPurchaseInvoiceDraftCommand(draft.Id), CancellationToken.None);
        Assert.True(first.IsSuccess);

        var second = await discardHandler.HandleAsync(new DiscardPurchaseInvoiceDraftCommand(draft.Id), CancellationToken.None);

        Assert.True(second.IsFailure);
        Assert.Equal(ErrorType.BusinessRule, second.Error!.Type);
        Assert.Equal("PurchaseInvoiceDraft.NotPendingReview", second.Error.Code);
    }
}
