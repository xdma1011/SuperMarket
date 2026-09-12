using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Catalog.CreateProductCategory;
using SupermarketSystem.Application.Common.Results;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Catalog;

/// <summary>
/// اختبار Handler مباشر (بلا HTTP) — يحل CreateProductCategoryHandler فعليًا
/// من نفس حاوية DI اللي الـAPI الحقيقي بيستخدمها (Application + Infrastructure
/// الحقيقيين معًا)، بس ضد قاعدة بيانات الاختبار. هذا النمط أدق من موك
/// IApplicationDbContext لأنه بيلمس فعليًا ترجمة LINQ إلى SQL الحقيقية —
/// بالضبط نوع الخطأ اللي CLAUDE.md §3.1 بيحذّر منه (enum.ToString() داخل
/// Select() ما بينترجم دايمًا زي ما نتوقع).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class CreateProductCategoryTests : IntegrationTestBase
{
    public CreateProductCategoryTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task إنشاء_تصنيف_بدون_أب_ينجح()
    {
        using var scope = CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<CreateProductCategoryHandler>();

        var result = await handler.HandleAsync(
            new CreateProductCategoryCommand("مشروبات", ParentCategoryId: null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("مشروبات", result.Value.Name);
        Assert.NotEqual(Guid.Empty, result.Value.CategoryId);
    }

    [Fact]
    public async Task إنشاء_تصنيف_باسم_فاضي_يفشل_بخطأ_تحقق()
    {
        using var scope = CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<CreateProductCategoryHandler>();

        var result = await handler.HandleAsync(
            new CreateProductCategoryCommand("   ", ParentCategoryId: null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Equal("ProductCategory.NameRequired", result.Error.Code);
    }

    [Fact]
    public async Task إنشاء_تصنيف_بأب_غير_موجود_يفشل_بـNotFound()
    {
        using var scope = CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<CreateProductCategoryHandler>();

        var result = await handler.HandleAsync(
            new CreateProductCategoryCommand("فرعي", ParentCategoryId: Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
    }

    [Fact]
    public async Task إنشاء_تصنيف_فرعي_بأب_موجود_ينجح()
    {
        using var scope = CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<CreateProductCategoryHandler>();

        var parent = await handler.HandleAsync(new CreateProductCategoryCommand("أغذية", null), CancellationToken.None);
        Assert.True(parent.IsSuccess);

        var child = await handler.HandleAsync(
            new CreateProductCategoryCommand("معلبات", parent.Value.CategoryId),
            CancellationToken.None);

        Assert.True(child.IsSuccess);
    }
}
