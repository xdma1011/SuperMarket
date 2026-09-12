using System.Net;
using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Purchasing.PurchaseInvoiceDrafts;
using SupermarketSystem.Domain.Purchasing;
using SupermarketSystem.Infrastructure.Persistence;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Purchasing;

/// <summary>
/// اختبار HTTP لـ PurchaseInvoiceDraftEndpoints.cs - عدا endpoint الرفع
/// من صورة (from-image)، لأنه بيعتمد فعليًا على IInvoiceExtractionService
/// (ذكاء اصطناعي خارجي حقيقي بلا بديل اختباري بالـDI) - راجع ملاحظة
/// PurchaseInvoiceDraftTests.cs.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PurchaseInvoiceDraftEndpointsTests : IntegrationTestBase
{
    public PurchaseInvoiceDraftEndpointsTests(DatabaseFixture fixture) : base(fixture) { }

    private async Task<Guid> SeedDraftAsync()
    {
        using var scope = CreateScope();
        var db = CreateDbContext(scope);
        var draft = new PurchaseInvoiceDraft(
            Fixture.TestBranchId, "test/http-image.webp", "Test", "مورد HTTP مستخرَج", null, null, null,
            "JOD", null, "High",
            PurchaseInvoiceDraftItemsSerializer.SerializeWarnings(Array.Empty<string>()),
            PurchaseInvoiceDraftItemsSerializer.Serialize(Array.Empty<PurchaseInvoiceDraftItemDto>()),
            null, null);
        db.PurchaseInvoiceDrafts.Add(draft);
        await db.SaveChangesAsync();
        return draft.Id;
    }

    [Fact]
    public async Task قائمة_المسودات_بلا_توكن_ترجّع_401()
    {
        var client = CreateAnonymousClient();
        var response = await client.GetAsync("/api/v1/purchase-invoices/drafts");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task قائمة_المسودات_بتوكن_Master_Admin_ترجّع_200()
    {
        await SeedDraftAsync();
        var client = await CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/v1/purchase-invoices/drafts");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task تفاصيل_مسودة_موجودة_ترجّع_200()
    {
        var draftId = await SeedDraftAsync();
        var client = await CreateAuthenticatedClientAsync();
        var response = await client.GetAsync($"/api/v1/purchase-invoices/drafts/{draftId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task حذف_مسودة_موجودة_ينجح_بـ204()
    {
        var draftId = await SeedDraftAsync();
        var client = await CreateAuthenticatedClientAsync();
        var response = await client.DeleteAsync($"/api/v1/purchase-invoices/drafts/{draftId}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task حذف_مسودة_غير_موجودة_يرجّع_404()
    {
        var client = await CreateAuthenticatedClientAsync();
        var response = await client.DeleteAsync($"/api/v1/purchase-invoices/drafts/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
