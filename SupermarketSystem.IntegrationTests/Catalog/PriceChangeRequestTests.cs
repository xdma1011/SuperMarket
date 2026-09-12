using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Catalog.CreateProduct;
using SupermarketSystem.Application.Catalog.CreateProductBranch;
using SupermarketSystem.Application.Catalog.CreateProductCategory;
using SupermarketSystem.Application.Catalog.DecidePriceChangeRequest;
using SupermarketSystem.Application.Catalog.GetPendingPriceChangeRequests;
using SupermarketSystem.Application.Catalog.RequestPriceChange;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Identity;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Catalog;

/// <summary>
/// السماح بثلاث مستويات لتغيير سعر بيع (راجع PermissionCodes.ChangeSellingPriceDirect/
/// RequestSellingPriceChange بالـApplication وتعليق PriceChangeRequest بالـDomain):
/// Direct يغيّر فورًا، RequestOnly (بلا Direct) يحتاج موافقة صريحة.
/// المستخدم Master Admin عنده الصلاحيتين معًا، فلازم مستخدم اختباري محدود
/// الصلاحيات (Catalog.RequestPriceChange بس) لنقدر نختبر مسار "بانتظار
/// الموافقة" فعليًا - القرار بين المسارين يُحسم Runtime حسب صلاحيات
/// المستخدم الحقيقي المرسِل، فلازم HTTP حقيقي بتوكن حقيقي (لا استدعاء
/// Handler مباشر بلا HttpContext، لأنه ICurrentUserContext.UserId بيرجع
/// null دايمًا بلا طلب HTTP فعلي).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PriceChangeRequestTests : IntegrationTestBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public PriceChangeRequestTests(DatabaseFixture fixture) : base(fixture) { }

    private async Task<Guid> SeedProductBranchAsync(IServiceScope scope, decimal initialPrice = 5m)
    {
        var categoryHandler = scope.ServiceProvider.GetRequiredService<CreateProductCategoryHandler>();
        var category = await categoryHandler.HandleAsync(new CreateProductCategoryCommand("تصنيف", null), CancellationToken.None);

        var productHandler = scope.ServiceProvider.GetRequiredService<CreateProductHandler>();
        var product = await productHandler.HandleAsync(
            new CreateProductCommand(
                "منتج", null, category.Value.CategoryId, false, null, null,
                new[] { new CreateProductUnitDto("قطعة", 1m, true) },
                Array.Empty<CreateProductBarcodeDto>()),
            CancellationToken.None);

        var branchHandler = scope.ServiceProvider.GetRequiredService<CreateProductBranchHandler>();
        var productBranch = await branchHandler.HandleAsync(
            new CreateProductBranchCommand(product.Value.ProductId, Fixture.TestBranchId, initialPrice, null, null),
            CancellationToken.None);

        return productBranch.Value.ProductBranchId;
    }

    /// <summary>
    /// مستخدم اختباري بصلاحية Catalog.RequestPriceChange بس (بلا
    /// ChangePriceDirect) - Users/Roles/UserRoles/UserBranches مستثناة من
    /// تصفير Respawn (راجع DatabaseFixture)، فالاسم لازم فريد بكل تشغيلة.
    /// </summary>
    private async Task<(string Username, string Password)> CreateRequestOnlyUserAsync(IServiceScope scope)
    {
        var db = CreateDbContext(scope);
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var permission = await db.Permissions.AsNoTracking()
            .SingleAsync(p => p.Code == PermissionCodes.RequestSellingPriceChange);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var role = new Role($"طالب-تعديل-سعر-{suffix}", "صلاحية طلب تعديل سعر فقط - بلا موافقة مباشرة (اختبار)");
        role.GrantPermission(permission.Id);
        db.Roles.Add(role);

        const string password = "RequestOnly_P@ss123!";
        var username = $"price.requester.{suffix}";
        var user = new User("مستخدم طلب سعر", username, $"{username}@local.invalid");
        user.SetPasswordHash(passwordHasher.Hash(password));
        db.Users.Add(user);

        await db.SaveChangesAsync(CancellationToken.None);

        db.UserRoles.Add(new UserRole(user.Id, role.Id, branchId: null));
        db.UserBranches.Add(new UserBranch(user.Id, Fixture.TestBranchId, isDefault: true));
        await db.SaveChangesAsync(CancellationToken.None);

        return (username, password);
    }

    private async Task<HttpClient> LoginAsAsync(string username, string password)
    {
        var client = CreateAnonymousClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Username = username,
            Password = password,
            AppType = "Admin",
            BranchId = Fixture.TestBranchId
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());
        return client;
    }

    [Fact]
    public async Task مستخدم_عنده_صلاحية_مباشرة_يطبق_السعر_الجديد_فورًا()
    {
        Guid productBranchId;
        using (var scope = CreateScope())
        {
            productBranchId = await SeedProductBranchAsync(scope, initialPrice: 5m);
        }

        // Master Admin عنده Catalog.ChangePriceDirect ضمن كل الصلاحيات.
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/products/{Guid.NewGuid()}/branches/{productBranchId}/price-change-requests",
            new { RequestedPrice = 9.5m });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.True(body.GetProperty("applied").GetBoolean());

        using var verifyScope = CreateScope();
        verifyScope.ActAsCrossBranchUser();
        var db = CreateDbContext(verifyScope);
        var productBranch = await db.ProductBranches.AsNoTracking().SingleAsync(pb => pb.Id == productBranchId);
        Assert.Equal(9.5m, productBranch.SellingPrice);
    }

    [Fact]
    public async Task مستخدم_بلا_صلاحية_مباشرة_يبقى_طلبه_بانتظار_الموافقة_والسعر_لا_يتغيّر()
    {
        Guid productBranchId;
        (string Username, string Password) requester;
        using (var scope = CreateScope())
        {
            productBranchId = await SeedProductBranchAsync(scope, initialPrice: 5m);
            requester = await CreateRequestOnlyUserAsync(scope);
        }

        var client = await LoginAsAsync(requester.Username, requester.Password);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/products/{Guid.NewGuid()}/branches/{productBranchId}/price-change-requests",
            new { RequestedPrice = 12m });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.False(body.GetProperty("applied").GetBoolean());
        var requestId = body.GetProperty("requestId").GetGuid();

        using (var verifyScope = CreateScope())
        {
            verifyScope.ActAsCrossBranchUser();
            var db = CreateDbContext(verifyScope);
            var productBranch = await db.ProductBranches.AsNoTracking().SingleAsync(pb => pb.Id == productBranchId);
            Assert.Equal(5m, productBranch.SellingPrice);
        }

        // قائمة المراجعة (لمن يقدر يوافق) لازم تُظهر الطلب المعلَّق.
        var adminClient = await CreateAuthenticatedClientAsync();
        var pendingResponse = await adminClient.GetAsync("/api/v1/price-change-requests");
        Assert.Equal(HttpStatusCode.OK, pendingResponse.StatusCode);
        var pending = await pendingResponse.Content.ReadFromJsonAsync<List<PriceChangeRequestListItemDto>>(JsonOptions);
        Assert.Contains(pending!, r => r.Id == requestId);

        // الموافقة تطبّق السعر فعليًا الآن.
        var approveResponse = await adminClient.PostAsJsonAsync(
            $"/api/v1/price-change-requests/{requestId}/approve", new { Note = (string?)null });
        Assert.Equal(HttpStatusCode.NoContent, approveResponse.StatusCode);

        using var finalScope = CreateScope();
        finalScope.ActAsCrossBranchUser();
        var finalDb = CreateDbContext(finalScope);
        var finalProductBranch = await finalDb.ProductBranches.AsNoTracking().SingleAsync(pb => pb.Id == productBranchId);
        Assert.Equal(12m, finalProductBranch.SellingPrice);
    }

    [Fact]
    public async Task رفض_طلب_معلَّق_لا_يغيّر_السعر()
    {
        Guid productBranchId;
        using var scope = CreateScope();
        // ProductBranch كيان Branch-owned - RequestPriceChangeHandler
        // بيقرأه مباشرة (بلا HTTP هون) - راجع تعليق TestAuthContext.
        scope.ActAsCrossBranchUser();
        productBranchId = await SeedProductBranchAsync(scope, initialPrice: 5m);

        var requestHandler = scope.ServiceProvider.GetRequiredService<RequestPriceChangeHandler>();
        var requestResult = await requestHandler.HandleAsync(
            new RequestPriceChangeCommand(productBranchId, 20m), CancellationToken.None);
        Assert.True(requestResult.IsSuccess);
        // بلا HttpContext، ICurrentUserContext.UserId == null فبيتحسم دايمًا
        // كـ"بانتظار موافقة" - هذا بالضبط سلوك الحماية الافتراضي الآمن.
        Assert.False(requestResult.Value.Applied);

        var rejectHandler = scope.ServiceProvider.GetRequiredService<RejectPriceChangeRequestHandler>();
        var rejectResult = await rejectHandler.HandleAsync(
            new RejectPriceChangeRequestCommand(requestResult.Value.RequestId, "سعر غير منطقي"), CancellationToken.None);

        Assert.True(rejectResult.IsSuccess);

        var db = CreateDbContext(scope);
        var productBranch = await db.ProductBranches.AsNoTracking().SingleAsync(pb => pb.Id == productBranchId);
        Assert.Equal(5m, productBranch.SellingPrice);
    }

    [Fact]
    public async Task طلب_بسعر_سالب_يفشل_بخطأ_تحقق()
    {
        using var scope = CreateScope();
        var productBranchId = await SeedProductBranchAsync(scope);
        var handler = scope.ServiceProvider.GetRequiredService<RequestPriceChangeHandler>();

        var result = await handler.HandleAsync(new RequestPriceChangeCommand(productBranchId, -1m), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Equal("PriceChangeRequest.PriceNegative", result.Error.Code);
    }

    [Fact]
    public async Task الموافقة_على_طلب_غير_موجود_يفشل_بـNotFound()
    {
        using var scope = CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ApprovePriceChangeRequestHandler>();

        var result = await handler.HandleAsync(new ApprovePriceChangeRequestCommand(Guid.NewGuid(), null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
        Assert.Equal("PriceChangeRequest.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task الموافقة_على_طلب_تم_البت_فيه_أصلًا_يفشل_بـConflict()
    {
        using var scope = CreateScope();
        scope.ActAsCrossBranchUser();
        var productBranchId = await SeedProductBranchAsync(scope);
        var requestHandler = scope.ServiceProvider.GetRequiredService<RequestPriceChangeHandler>();
        var requestResult = await requestHandler.HandleAsync(new RequestPriceChangeCommand(productBranchId, 8m), CancellationToken.None);

        var rejectHandler = scope.ServiceProvider.GetRequiredService<RejectPriceChangeRequestHandler>();
        await rejectHandler.HandleAsync(new RejectPriceChangeRequestCommand(requestResult.Value.RequestId, null), CancellationToken.None);

        var approveHandler = scope.ServiceProvider.GetRequiredService<ApprovePriceChangeRequestHandler>();
        var approveResult = await approveHandler.HandleAsync(
            new ApprovePriceChangeRequestCommand(requestResult.Value.RequestId, null), CancellationToken.None);

        Assert.True(approveResult.IsFailure);
        Assert.Equal(ErrorType.Conflict, approveResult.Error!.Type);
        Assert.Equal("PriceChangeRequest.NotPending", approveResult.Error.Code);
    }
}
