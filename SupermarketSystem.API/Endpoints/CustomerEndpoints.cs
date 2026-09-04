using SupermarketSystem.API.Common;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Application.Customers.BlockCustomer;
using SupermarketSystem.Application.Customers.FileComplaint;
using SupermarketSystem.Application.Customers.GetCustomers;
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
}
