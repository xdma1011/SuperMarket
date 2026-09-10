using SupermarketSystem.API.Common;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Catalog.CreateProduct;
using SupermarketSystem.Application.Catalog.CreateProductBranch;
using SupermarketSystem.Application.Catalog.CreateProductCategory;
using SupermarketSystem.Application.Catalog.GetProductCategories;
using SupermarketSystem.Application.Catalog.GetProducts;
using SupermarketSystem.Application.Catalog.AddProductUnit;
using SupermarketSystem.Application.Catalog.UpdateProductUnitBarcode;
using SupermarketSystem.Application.Catalog.GetProductBranches;
using SupermarketSystem.Application.Catalog.GetProductByBarcode;
using SupermarketSystem.Application.Catalog.GetProductUnits;
using SupermarketSystem.Application.Catalog.SetProductComplimentaryAllowed;
using SupermarketSystem.Application.Catalog.SetProductBranchAvailability;
using SupermarketSystem.Application.Catalog.UpdateProduct;
using SupermarketSystem.Application.Catalog.UpdateProductCategory;
using SupermarketSystem.Application.Catalog.RequestPriceChange;
using SupermarketSystem.Application.Catalog.GetPendingPriceChangeRequests;
using SupermarketSystem.Application.Catalog.DecidePriceChangeRequest;
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

        products.MapPut("/{productId:guid}/units/{unitId:guid}/barcode", async (
            Guid productId,
            Guid unitId,
            UpdateProductUnitBarcodeRequest request,
            UpdateProductUnitBarcodeHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(
                new UpdateProductUnitBarcodeCommand(productId, unitId, request.BarcodeValue), cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("UpdateProductUnitBarcode")
        .WithSummary("يعدّل أو يحذف باركود وحدة موجودة أصلًا - كان بلا أي طريقة تصحيح بعد الإنشاء.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

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

        products.MapGet("/{productId:guid}/branches", async (
            Guid productId,
            GetProductBranchesHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new GetProductBranchesQuery(productId), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetProductBranches")
        .WithSummary("أي فروع مربوط فيها هذا المنتج حاليًا، بسعره بكل فرع - كانت ناقصة كليًا.")
        .Produces<IReadOnlyList<ProductBranchItemDto>>(StatusCodes.Status200OK);

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

        products.MapPost("/{productId:guid}/branches/{productBranchId:guid}/availability", async (
            Guid productId,
            Guid productBranchId,
            SetProductBranchAvailabilityRequest request,
            SetProductBranchAvailabilityHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(
                new SetProductBranchAvailabilityCommand(productBranchId, request.IsAvailableForSale), cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("SetProductBranchAvailability")
        .WithSummary("يفعّل/يوقف بيع منتج بفرع معيّن - كانت مفقودة كليًا (سبب خطأ Sale.ProductNotActive الغامض بلا أي طريقة تعديله من الواجهة).")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // خارج مجموعة products عمدًا - صلاحيتها Catalog.Manage، بينما تعديل
        // السعر له صلاحيتان منفصلتان (Direct/Request)، والفلاتر تتراكم (AND)
        // لا تتجاوز بعض (CLAUDE.md §3.4) - لو حطينا هون جوّا المجموعة كان
        // رح يصير لازم Catalog.Manage و[صلاحية السعر] معًا، بينما القصد
        // إحداهما لحالها.
        app.MapPost("/api/v1/products/{productId:guid}/branches/{productBranchId:guid}/price-change-requests", async (
            Guid productId,
            Guid productBranchId,
            RequestPriceChangeRequest request,
            RequestPriceChangeHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new RequestPriceChangeCommand(productBranchId, request.RequestedPrice), cancellationToken);
            return result.ToHttpResult();
        })
        .WithTags("Catalog")
        .RequirePermission(PermissionCodes.RequestSellingPriceChange)
        .WithName("RequestPriceChange")
        .WithSummary("يطلب تعديل سعر بيع منتج بفرع - يتغيّر فورًا لو عند المستخدم Catalog.ChangePriceDirect، وإلا يضل بانتظار موافقة صريحة.")
        .Produces<RequestPriceChangeResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet("/api/v1/price-change-requests", async (
            GetPendingPriceChangeRequestsHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new GetPendingPriceChangeRequestsQuery(), cancellationToken);
            return Results.Ok(result);
        })
        .WithTags("Catalog")
        .RequirePermission(PermissionCodes.ChangeSellingPriceDirect)
        .WithName("GetPendingPriceChangeRequests")
        .WithSummary("قائمة طلبات تعديل السعر بانتظار الموافقة - لمن يقدر يوافق فقط (Catalog.ChangePriceDirect).")
        .Produces<IReadOnlyList<PriceChangeRequestListItemDto>>(StatusCodes.Status200OK);

        app.MapPost("/api/v1/price-change-requests/{requestId:guid}/approve", async (
            Guid requestId,
            DecidePriceChangeRequestRequest request,
            ApprovePriceChangeRequestHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new ApprovePriceChangeRequestCommand(requestId, request.Note), cancellationToken);
            return result.ToHttpResult();
        })
        .WithTags("Catalog")
        .RequirePermission(PermissionCodes.ChangeSellingPriceDirect)
        .WithName("ApprovePriceChangeRequest")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        app.MapPost("/api/v1/price-change-requests/{requestId:guid}/reject", async (
            Guid requestId,
            DecidePriceChangeRequestRequest request,
            RejectPriceChangeRequestHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new RejectPriceChangeRequestCommand(requestId, request.Note), cancellationToken);
            return result.ToHttpResult();
        })
        .WithTags("Catalog")
        .RequirePermission(PermissionCodes.ChangeSellingPriceDirect)
        .WithName("RejectPriceChangeRequest")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    /// <summary>ProductId comes from the route, not the body — this is the request-body shape for POST /products/{productId}/branches.</summary>
    public sealed record CreateProductBranchRequest(Guid BranchId, decimal SellingPrice, decimal? MinimumStock, decimal? MaximumStock);

    public sealed record SetProductComplimentaryAllowedRequest(bool Allowed);

    public sealed record SetProductBranchAvailabilityRequest(bool IsAvailableForSale);
    public sealed record RequestPriceChangeRequest(decimal RequestedPrice);
    public sealed record DecidePriceChangeRequestRequest(string? Note);
    public sealed record UpdateProductCategoryRequest(string Name);
    public sealed record UpdateProductRequest(string Name, Guid CategoryId, decimal? SuggestedRetailPrice, int? ExpectedShelfLifeDays);
    public sealed record AddProductUnitRequest(string UnitName, decimal ConversionFactorToBase, string? BarcodeValue);
    public sealed record UpdateProductUnitBarcodeRequest(string? BarcodeValue);
}
