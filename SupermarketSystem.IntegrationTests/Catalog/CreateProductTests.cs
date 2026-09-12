using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Catalog.CreateProduct;
using SupermarketSystem.Application.Catalog.CreateProductCategory;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Infrastructure.Persistence;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Catalog;

/// <summary>
/// اختبار Handler مباشر لإنشاء منتج مع وحداته وباركوداته كـaggregate واحد.
/// أهم قاعدة عمل هون: منتج بلا SuggestedRetailPrice ما بيُربط بأي فرع
/// تلقائيًا (CLAUDE.md §1.8) - هاي فجوة حقيقية واجهها صاحب المشروع
/// (كتالوج فاضي بالكاشير)، فلازم تبقى مغطّاة باختبار صريح للحالتين.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class CreateProductTests : IntegrationTestBase
{
    public CreateProductTests(DatabaseFixture fixture) : base(fixture) { }

    private async Task<Guid> CreateCategoryAsync(IServiceScope scope, string name = "أغذية")
    {
        var categoryHandler = scope.ServiceProvider.GetRequiredService<CreateProductCategoryHandler>();
        var result = await categoryHandler.HandleAsync(new CreateProductCategoryCommand(name, null), CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Value.CategoryId;
    }

    [Fact]
    public async Task إنشاء_منتج_بسعر_مقترح_يربطه_تلقائيًا_بكل_الفروع_الفعالة()
    {
        using var scope = CreateScope();
        var categoryId = await CreateCategoryAsync(scope);
        var handler = scope.ServiceProvider.GetRequiredService<CreateProductHandler>();

        var command = new CreateProductCommand(
            "حليب كامل الدسم", "1 لتر", categoryId, IsBatchTracked: false,
            SuggestedRetailPrice: 2.50m, ExpectedShelfLifeDays: 14,
            Units: new[] { new CreateProductUnitDto("قطعة", 1m, true) },
            Barcodes: Array.Empty<CreateProductBarcodeDto>());

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("حليب كامل الدسم", result.Value.Name);

        // ProductBranch كيان Branch-owned - راجع تعليق TestAuthContext.
        var db = CreateDbContext(scope);
        var productBranches = await db.ProductBranches.AsNoTracking().IgnoreQueryFilters()
            .Where(pb => pb.ProductId == result.Value.ProductId)
            .ToListAsync();

        var activeBranchCount = await db.Branches.AsNoTracking().CountAsync(b => b.IsActive);
        // لازم يربط بكل فرع فعّال - عدد الفروع الفعّالة نفسه بيكبر بتراكم
        // فروع اختبارية عبر تشغيلات سابقة (Branches مستثناة من التصفير)،
        // فالمقارنة هون بالعدد الفعلي الحالي لا رقم ثابت.
        Assert.Equal(activeBranchCount, productBranches.Count);

        var testBranchLink = Assert.Single(productBranches, pb => pb.BranchId == Fixture.TestBranchId);
        Assert.Equal(2.50m, testBranchLink.SellingPrice);
    }

    [Fact]
    public async Task إنشاء_منتج_بلا_سعر_مقترح_لا_ينشئ_أي_ربط_فرع()
    {
        using var scope = CreateScope();
        var categoryId = await CreateCategoryAsync(scope);
        var handler = scope.ServiceProvider.GetRequiredService<CreateProductHandler>();

        var command = new CreateProductCommand(
            "منتج بلا سعر", null, categoryId, IsBatchTracked: false,
            SuggestedRetailPrice: null, ExpectedShelfLifeDays: null,
            Units: new[] { new CreateProductUnitDto("قطعة", 1m, true) },
            Barcodes: Array.Empty<CreateProductBarcodeDto>());

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsSuccess);

        var db = CreateDbContext(scope);
        var hasAnyBranchLink = await db.ProductBranches.AsNoTracking()
            .AnyAsync(pb => pb.ProductId == result.Value.ProductId);

        // هذا بالضبط سبب "الكتالوج فاضي بالكاشير رغم تعريف منتجات" - منتج
        // بلا سعر يضل غير مرئي كليًا للكاشير لحد ما يُربط يدويًا بفرع.
        Assert.False(hasAnyBranchLink);
    }

    [Fact]
    public async Task إنشاء_منتج_باسم_فاضي_يفشل_بخطأ_تحقق()
    {
        using var scope = CreateScope();
        var categoryId = await CreateCategoryAsync(scope);
        var handler = scope.ServiceProvider.GetRequiredService<CreateProductHandler>();

        var command = new CreateProductCommand(
            "   ", null, categoryId, false, null, null,
            new[] { new CreateProductUnitDto("قطعة", 1m, true) },
            Array.Empty<CreateProductBarcodeDto>());

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Equal("Product.NameRequired", result.Error.Code);
    }

    [Fact]
    public async Task إنشاء_منتج_بأكثر_من_وحدة_أساسية_يفشل_بخطأ_تحقق()
    {
        using var scope = CreateScope();
        var categoryId = await CreateCategoryAsync(scope);
        var handler = scope.ServiceProvider.GetRequiredService<CreateProductHandler>();

        var command = new CreateProductCommand(
            "منتج بوحدتين أساسيتين", null, categoryId, false, null, null,
            new[]
            {
                new CreateProductUnitDto("قطعة", 1m, true),
                new CreateProductUnitDto("كرتونة", 12m, true)
            },
            Array.Empty<CreateProductBarcodeDto>());

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Product.ExactlyOneBaseUnit", result.Error!.Code);
    }

    [Fact]
    public async Task إنشاء_منتج_بتصنيف_غير_موجود_يفشل_بـNotFound()
    {
        using var scope = CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<CreateProductHandler>();

        var command = new CreateProductCommand(
            "منتج بتصنيف وهمي", null, Guid.NewGuid(), false, null, null,
            new[] { new CreateProductUnitDto("قطعة", 1m, true) },
            Array.Empty<CreateProductBarcodeDto>());

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
        Assert.Equal("Product.CategoryNotFound", result.Error.Code);
    }

    [Fact]
    public async Task إنشاء_منتج_بباركود_مستخدم_أصلًا_يفشل_بـConflict()
    {
        using var scope = CreateScope();
        var categoryId = await CreateCategoryAsync(scope);
        var handler = scope.ServiceProvider.GetRequiredService<CreateProductHandler>();

        var firstCommand = new CreateProductCommand(
            "منتج أول", null, categoryId, false, null, null,
            new[] { new CreateProductUnitDto("قطعة", 1m, true) },
            new[] { new CreateProductBarcodeDto("1234567890123", "قطعة") });
        var firstResult = await handler.HandleAsync(firstCommand, CancellationToken.None);
        Assert.True(firstResult.IsSuccess);

        var secondCommand = new CreateProductCommand(
            "منتج ثاني بنفس الباركود", null, categoryId, false, null, null,
            new[] { new CreateProductUnitDto("قطعة", 1m, true) },
            new[] { new CreateProductBarcodeDto("1234567890123", "قطعة") });
        var secondResult = await handler.HandleAsync(secondCommand, CancellationToken.None);

        Assert.True(secondResult.IsFailure);
        Assert.Equal(ErrorType.Conflict, secondResult.Error!.Type);
        Assert.Equal("Product.BarcodeAlreadyExists", secondResult.Error.Code);
    }
}
