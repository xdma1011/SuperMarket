using SupermarketSystem.API.Common;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Ordering.Coupons;

namespace SupermarketSystem.API.Endpoints;

/// <summary>
/// كوبونات خصم تطبيق الزبائن (29/9/2026) - الإدارة تحت Customers.Manage. جهة الزبون (قائمة كوبوناته وفحص الكود)
/// AllowAnonymous بنفس تحذير OrderingEndpoints.cs (بلا تحقق هوية زبون حقيقي بعد) - برّا المجموعة (§3.4).
/// </summary>
public static class CouponEndpoints
{
    public static IEndpointRouteBuilder MapCouponEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/coupons")
            .WithTags("Coupons")
            .RequirePermission(PermissionCodes.CustomersManage);

        group.MapGet("/", async (GetCouponsHandler handler, CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(cancellationToken)))
            .WithName("GetCoupons")
            .WithSummary("الكوبونات بحالتها وعدد استعمالها ومجموع خصمها الفعلي.")
            .Produces<IReadOnlyList<CouponDto>>(StatusCodes.Status200OK);

        group.MapPost("/", async (CreateCouponCommand command, CreateCouponHandler handler, CancellationToken cancellationToken) =>
            {
                var result = await handler.HandleAsync(command, cancellationToken);
                return result.ToHttpResult(id => Results.Created($"/api/v1/coupons/{id}", new { couponId = id }));
            })
            .WithName("CreateCoupon")
            .WithSummary("كوبون جديد - لزبون معيّن (customerId) أو للكل.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{couponId:guid}", async (Guid couponId, UpdateCouponRequest request, UpdateCouponHandler handler, CancellationToken cancellationToken) =>
                (await handler.HandleAsync(new UpdateCouponCommand(
                    couponId, request.Title, request.DiscountType, request.Value, request.MaxDiscountAmount, request.MinOrderAmount,
                    request.StartAtUtc, request.EndAtUtc, request.MaxUsesPerCustomer, request.MaxTotalUses), cancellationToken)).ToHttpResult())
            .WithName("UpdateCoupon")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{couponId:guid}/active", async (Guid couponId, SetCouponActiveRequest request, SetCouponActiveHandler handler, CancellationToken cancellationToken) =>
                (await handler.HandleAsync(new SetCouponActiveCommand(couponId, request.IsActive), cancellationToken)).ToHttpResult())
            .WithName("SetCouponActive");

        group.MapPost("/{couponId:guid}/send", async (Guid couponId, SendCouponRequest request, SendCouponHandler handler, CancellationToken cancellationToken) =>
                (await handler.HandleAsync(new SendCouponCommand(couponId, request.CustomerId), cancellationToken)).ToHttpResult(r => Results.Ok(r)))
            .WithName("SendCoupon")
            .WithSummary("إرسال يدوي (إشعار التطبيق + تلغرام): لزبون واحد، أو للكل.")
            .Produces<SendCouponResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // ⚠️ نفس تحذير OrderingEndpoints.cs - بلا تحقق هوية زبون حقيقي بعد.
        app.MapGet("/api/v1/customers/{customerId:guid}/coupons", async (Guid customerId, GetCustomerCouponsHandler handler, CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(customerId, cancellationToken)))
            .WithName("GetCustomerCoupons")
            .WithTags("Coupons")
            .AllowAnonymous()
            .WithSummary("⚠️ مؤقت بلا تحقق هوية حقيقي - كوبونات الزبون الشغّالة (المخصصة إله + العامة) بالتطبيق.")
            .Produces<IReadOnlyList<CustomerCouponDto>>(StatusCodes.Status200OK);

        app.MapPost("/api/v1/customers/{customerId:guid}/coupons/preview", async (
                Guid customerId, PreviewCouponRequest request, PreviewCouponHandler handler, CancellationToken cancellationToken) =>
                (await handler.HandleAsync(new PreviewCouponQuery(customerId, request.Code, request.EstimatedTotal), cancellationToken)).ToHttpResult())
            .WithName("PreviewCoupon")
            .WithTags("Coupons")
            .AllowAnonymous()
            .WithSummary("⚠️ مؤقت بلا تحقق هوية حقيقي - فحص كود خصم على مجموع السلة قبل الطلب (بلا حجز).")
            .Produces<PreviewCouponResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return app;
    }

    public sealed record UpdateCouponRequest(
        string Title, Domain.Ordering.CouponDiscountType DiscountType, decimal Value, decimal? MaxDiscountAmount, decimal MinOrderAmount,
        DateTime StartAtUtc, DateTime EndAtUtc, int MaxUsesPerCustomer, int? MaxTotalUses);

    public sealed record SetCouponActiveRequest(bool IsActive);

    public sealed record SendCouponRequest(Guid? CustomerId);

    public sealed record PreviewCouponRequest(string Code, decimal EstimatedTotal);
}
