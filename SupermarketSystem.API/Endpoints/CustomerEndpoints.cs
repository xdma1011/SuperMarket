using SupermarketSystem.API.Common;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Application.Customers.BlockCustomer;
using SupermarketSystem.Application.Customers.FileComplaint;
using SupermarketSystem.Application.Customers.GetCustomerQrToken;
using SupermarketSystem.Application.Customers.GetCustomerLoyaltyBalance;
using SupermarketSystem.Application.Customers.GetCustomers;
using SupermarketSystem.Application.Customers.RedeemLoyaltyPoints;
using SupermarketSystem.Application.Customers.RegisterCustomerDeviceToken;
using SupermarketSystem.Application.Customers.ResolveCustomerQrToken;
using SupermarketSystem.Domain.Customers;
using SupermarketSystem.Application.Customers.UnblockCustomer;

namespace SupermarketSystem.API.Endpoints;

public static class CustomerEndpoints
{
    public static IEndpointRouteBuilder MapCustomerEndpoints(this IEndpointRouteBuilder app)
    {
        // ⚠️ نفس تحذير OrderingEndpoints.cs - بلا تحقق هوية حقيقي بعد.
        app.MapPost("/api/v1/complaints", async (
            FileComplaintCommand command,
            FileComplaintHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(command, cancellationToken);
            return result.ToHttpResult(response => Results.Created($"/api/v1/complaints/{response.ComplaintId}", response));
        })
        .WithName("FileComplaint")
        .WithTags("Customers")
        .AllowAnonymous()
        .WithSummary("⚠️ مؤقت بلا تحقق هوية حقيقي - يسجّل شكوى زبون، مرتبطة بطلب أو عامة.")
        .Produces<FileComplaintResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        // ⚠️ نفس تحذير OrderingEndpoints.cs - بلا تحقق هوية حقيقي بعد.
        app.MapGet("/api/v1/customers/{customerId:guid}/qr-token", async (
            Guid customerId,
            GetCustomerQrTokenHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new GetCustomerQrTokenQuery(customerId), cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("GetCustomerQrToken")
        .WithTags("Customers")
        .AllowAnonymous()
        .WithSummary("⚠️ مؤقت بلا تحقق هوية حقيقي - يولّد توكن QR ثابت لهوية الزبون يعرضه تطبيق الطلبات.")
        .Produces<GetCustomerQrTokenResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // صلاحية الكاشير (Sales.Create) لا صلاحية إدارة الزبائن - عمدًا
        // برّا مجموعة CustomersManage تحتها (CLAUDE.md §3.4، تفادي فخ AND).
        app.MapPost("/api/v1/customers/resolve-qr", async (
            ResolveCustomerQrTokenRequest request,
            ResolveCustomerQrTokenHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new ResolveCustomerQrTokenQuery(request.QrToken), cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("ResolveCustomerQrToken")
        .WithTags("Customers")
        .RequirePermission(PermissionCodes.SalesCreate)
        .WithSummary("الكاشير يمسح باركود الزبون فيرجّع هويته الفعلية - يمنع تلاعب رقم هاتف يُكتب يدويًا.")
        .Produces<ResolveCustomerQrTokenResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // ⚠️ نفس تحذير OrderingEndpoints.cs - بلا تحقق هوية حقيقي بعد.
        app.MapPost("/api/v1/customers/{customerId:guid}/device-tokens", async (
            Guid customerId,
            RegisterCustomerDeviceTokenRequest request,
            RegisterCustomerDeviceTokenHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(
                new RegisterCustomerDeviceTokenCommand(customerId, request.Token, request.Platform), cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("RegisterCustomerDeviceToken")
        .WithTags("Customers")
        .AllowAnonymous()
        .WithSummary("⚠️ مؤقت بلا تحقق هوية حقيقي - يسجّل توكن جهاز FCM لتفعيل إشعارات Push لتطبيق الطلبات.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // ⚠️ نفس تحذير OrderingEndpoints.cs - بلا تحقق هوية حقيقي بعد.
        app.MapGet("/api/v1/customers/{customerId:guid}/loyalty-balance", async (
            Guid customerId,
            GetCustomerLoyaltyBalanceHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new GetCustomerLoyaltyBalanceQuery(customerId), cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("GetCustomerLoyaltyBalance")
        .WithTags("Customers")
        .AllowAnonymous()
        .WithSummary("⚠️ مؤقت بلا تحقق هوية حقيقي - رصيد نقاط الولاء الحالي (محسوب حيًّا، راجع تعليق CustomerLoyaltyPointsEntry).")
        .Produces<GetCustomerLoyaltyBalanceResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // صلاحية الكاشير - تسجيل استبدال نقاط عملية يدوية بالكاشير، لا
        // ربط تلقائي بخصم فاتورة بعد (راجع تعليق RedeemLoyaltyPointsHandler).
        app.MapPost("/api/v1/customers/{customerId:guid}/loyalty/redeem", async (
            Guid customerId,
            RedeemLoyaltyPointsRequest request,
            RedeemLoyaltyPointsHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new RedeemLoyaltyPointsCommand(customerId, request.Points), cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("RedeemLoyaltyPoints")
        .WithTags("Customers")
        .RequirePermission(PermissionCodes.SalesCreate)
        .WithSummary("يسجّل استبدال نقاط ولاء بسطر دفتر سالب - لا يخصم تلقائيًا من فاتورة (راجع التحذير بالكود).")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // مجموعة إدارة الزبائن - عمدًا برّا المجموعة الأعلى (RequirePermission
        // على مستوى المجموعة، راجع CLAUDE.md §3.4) - الشكوى فوق ما تنحط
        // جوّاها لأنها AllowAnonymous.
        var group = app.MapGroup("/api/v1/customers").WithTags("Customers").RequirePermission(PermissionCodes.CustomersManage);

        group.MapGet("/", async (
            int? pageNumber, int? pageSize, string? search,
            GetCustomersHandler handler,
            CancellationToken cancellationToken) =>
        {
            var paging = PagingBinder.Build(pageNumber, pageSize, search, sortBy: null, sortDirection: null);
            var result = await handler.HandleAsync(new GetCustomersQuery(paging), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetCustomers")
        .WithSummary("قائمة الزبائن، مع بحث بالاسم أو الهاتف.")
        .Produces<PagedResult<CustomerListItemDto>>(StatusCodes.Status200OK);

        group.MapPost("/{customerId:guid}/block", async (
            Guid customerId,
            BlockCustomerHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new BlockCustomerCommand(customerId), cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("BlockCustomer")
        .WithSummary("يمنع الزبون من تقديم طلبات جديدة عبر تطبيق الزبائن - لا يمنع بيع POS عادي له.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{customerId:guid}/unblock", async (
            Guid customerId,
            UnblockCustomerHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new UnblockCustomerCommand(customerId), cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("UnblockCustomer")
        .WithSummary("يرفع الحظر عن الزبون.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    public sealed record ResolveCustomerQrTokenRequest(string QrToken);
    public sealed record RegisterCustomerDeviceTokenRequest(string Token, DevicePlatform Platform);
    public sealed record RedeemLoyaltyPointsRequest(int Points);
}
