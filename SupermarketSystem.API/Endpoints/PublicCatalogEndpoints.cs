using SupermarketSystem.API.Common;
using SupermarketSystem.Application.Catalog.GetPublicCatalog;
using SupermarketSystem.Application.Catalog.GetPublicCatalogCategories;
using SupermarketSystem.Application.Common.Pagination;

namespace SupermarketSystem.API.Endpoints;

/// <summary>
/// تصفّح الكتالوج العام لتطبيق الزبائن - بلا مصادقة (نفس مبدأ
/// GetPublicBranchesHandler: تصفّح المنتجات لا يحتاج تسجيل دخول، بس
/// تقديم الطلب فعليًا يحتاجه). يطبّق قاعدة إخفاء المخزون القليل
/// تلقائيًا (راجع GetPublicCatalogHandler).
/// </summary>
public static class PublicCatalogEndpoints
{
    public static IEndpointRouteBuilder MapPublicCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/catalog/products", async (
            Guid branchId, Guid? categoryId, string? search, int? pageNumber, int? pageSize,
            GetPublicCatalogHandler handler,
            CancellationToken cancellationToken) =>
        {
            var paging = PagingBinder.Build(pageNumber, pageSize, search, sortBy: null, sortDirection: null);
            var result = await handler.HandleAsync(new GetPublicCatalogQuery(branchId, categoryId, paging), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetPublicCatalog")
        .WithTags("PublicCatalog")
        .AllowAnonymous()
        .WithSummary("تصفّح منتجات فرع معيّن مع بحث وpagination - يخفي تلقائيًا منتجات بمخزون أقل من الحد الأدنى المناسب لفئتها السعرية.")
        .Produces<PagedResult<PublicCatalogItemDto>>(StatusCodes.Status200OK);

        app.MapGet("/api/v1/catalog/categories", async (
            Guid branchId,
            GetPublicCatalogCategoriesHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new GetPublicCatalogCategoriesQuery(branchId), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetPublicCatalogCategories")
        .WithTags("PublicCatalog")
        .AllowAnonymous()
        .WithSummary("التصنيفات اللي فيها منتج متاح واحد على الأقل بالفرع المحدَّد.")
        .Produces<IReadOnlyList<PublicCategoryDto>>(StatusCodes.Status200OK);

        return app;
    }
}
