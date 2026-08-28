using SupermarketSystem.API.Common;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Catalog.CreateProduct;
using SupermarketSystem.Application.Catalog.CreateProductBranch;
using SupermarketSystem.Application.Catalog.CreateProductCategory;
using SupermarketSystem.Application.Catalog.GetProductCategories;
using SupermarketSystem.Application.Catalog.GetProducts;
using SupermarketSystem.Application.Catalog.AddProductUnit;
using SupermarketSystem.Application.Catalog.GetProductByBarcode;
using SupermarketSystem.Application.Catalog.GetProductUnits;
using SupermarketSystem.Application.Catalog.SetProductComplimentaryAllowed;
using SupermarketSystem.Application.Catalog.UpdateProduct;
using SupermarketSystem.Application.Catalog.UpdateProductCategory;
using SupermarketSystem.Application.Common.Pagination;

namespace SupermarketSystem.API.Endpoints;

public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var categories = app.MapGroup("/api/v1/product-categories").WithTags("Catalog").RequirePermission(PermissionCodes.CatalogManage);

        categories.MapPost("/", async (
            CreateProductCategoryCommand command,
            CreateProductCategoryHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(command, cancellationToken);
            return result.ToHttpResult(response => Results.Created($"/api/v1/product-categories/{response.CategoryId}", response));
        })
        .WithName("CreateProductCategory")
        .Produces<CreateProductCategoryResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);

        categories.MapGet("/", async (
            int? pageNumber,
            int? pageSize,
            string? search,
            string? sortBy,
            string? sortDirection,
            GetProductCategoriesHandler handler,
            CancellationToken cancellationToken) =>
        {
            var paging = PagingBinder.Build(pageNumber, pageSize, search, sortBy, sortDirection);
            var result = await handler.HandleAsync(new GetProductCategoriesQuery(paging), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetProductCategories")
        .Produces<PagedResult<ProductCategoryListItemDto>>(StatusCodes.Status200OK);

        categories.MapPut("/{categoryId:guid}", async (
            Guid categoryId,
            UpdateProductCategoryRequest request,
            UpdateProductCategoryHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new UpdateProductCategoryCommand(categoryId, request.Name), cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("UpdateProductCategory")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        var products = app.MapGroup("/api/v1/products").WithTags("Catalog").RequirePermission(PermissionCodes.CatalogManage);

        products.MapPost("/", async (
            CreateProductCommand command,
            CreateProductHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(command, cancellationToken);
            return result.ToHttpResult(response => Results.Created($"/api/v1/products/{response.ProductId}", response));
        })
        .WithName("CreateProduct")
        .Produces<CreateProductResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        products.MapGet("/", async (
            int? pageNumber,
            int? pageSize,
            string? search,
            string? sortBy,
            string? sortDirection,
            Guid? categoryId,
            GetProductsHandler handler,
            CancellationToken cancellationToken) =>
        {
            var paging = PagingBinder.Build(pageNumber, pageSize, search, sortBy, sortDirection);
            var result = await handler.HandleAsync(new GetProductsQuery(paging, categoryId), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetProducts")
        .Produces<PagedResult<ProductListItemDto>>(StatusCodes.Status200OK);

        products.MapPut("/{productId:guid}", async (
            Guid productId,
            UpdateProductRequest request,
            UpdateProductHandler handler,
            CancellationToken cancellationToken) =>
        {
            var command = new UpdateProductCommand(
                productId, request.Name, request.CategoryId, request.SuggestedRetailPrice, request.ExpectedShelfLifeDays);
            var result = await handler.HandleAsync(command, cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("UpdateProduct")
        .WithSummary("يعدّل اسم المنتج وتصنيفه وسعره ومدة صلاحيته المتوقَّعة - لا وحدات القياس ولا حالة تتبّع الدفعات (قرارات بنيوية).")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        products.MapGet("/by-barcode/{barcodeValue}", async (
            string barcodeValue,
            GetProductByBarcodeHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new GetProductByBarcodeQuery(barcodeValue), cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("GetProductByBarcode")
        .WithSummary("يتحقق هل باركود ممسوح موجود أصلًا - أساس تدفّق 'امسح أول' بدل إدخال يدوي.")
        .Produces<ProductByBarcodeDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        products.MapPost("/{productId:guid}/units", async (
            Guid productId,
            AddProductUnitRequest request,
            AddProductUnitHandler handler,
            CancellationToken cancellationToken) =>
        {
            var command = new AddProductUnitCommand(
                productId, request.UnitName, request.ConversionFactorToBase, request.BarcodeValue);
            var result = await handler.HandleAsync(command, cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("AddProductUnit")
        .WithSummary("يضيف وحدة جديدة لمنتج موجود (طرد/كرتونة) مع باركود اختياري - الطرد يتحوّل تلقائيًا للوحدة الأساسية بمعامل التحويل.")
        .Produces<AddProductUnitResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        products.MapGet("/{productId:guid}/units", async (
            Guid productId,
            GetProductUnitsHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new GetProductUnitsQuery(productId), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetProductUnits")
        .WithSummary("وحدات منتج معيّن - أساس ربط سطر فاتورة شراء أو بيع بالوحدة الصحيحة.")
        .Produces<IReadOnlyList<ProductUnitDto>>(StatusCodes.Status200OK);

        products.MapPost("/{productId:guid}/complimentary-allowed", async (
            Guid productId,
            SetProductComplimentaryAllowedRequest request,
            SetProductComplimentaryAllowedHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(
                new SetProductComplimentaryAllowedCommand(productId, request.Allowed), cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("SetProductComplimentaryAllowed")
        .WithSummary("يفعّل/يعطّل إمكانية تسجيل هذا المنتج ضمن الضيافة - أول تعديل حقيقي بالنظام (لا إنشاء).")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        products.MapPost("/{productId:guid}/branches", async (
            Guid productId,
            CreateProductBranchRequest request,
            CreateProductBranchHandler handler,
            CancellationToken cancellationToken) =>
        {
            var command = new CreateProductBranchCommand(
                productId, request.BranchId, request.SellingPrice, request.MinimumStock, request.MaximumStock);

            var result = await handler.HandleAsync(command, cancellationToken);
            return result.ToHttpResult(response => Results.Created($"/api/v1/products/{productId}/branches/{response.ProductBranchId}", response));
        })
        .WithName("CreateProductBranch")
        .WithSummary("Onboards a product to a branch with an explicit selling price. No implicit fallback price exists.")
        .Produces<CreateProductBranchResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    /// <summary>ProductId comes from the route, not the body — this is the request-body shape for POST /products/{productId}/branches.</summary>
    public sealed record CreateProductBranchRequest(Guid BranchId, decimal SellingPrice, decimal? MinimumStock, decimal? MaximumStock);

    public sealed record SetProductComplimentaryAllowedRequest(bool Allowed);
    public sealed record UpdateProductCategoryRequest(string Name);
    public sealed record UpdateProductRequest(string Name, Guid CategoryId, decimal? SuggestedRetailPrice, int? ExpectedShelfLifeDays);
    public sealed record AddProductUnitRequest(string UnitName, decimal ConversionFactorToBase, string? BarcodeValue);
}
