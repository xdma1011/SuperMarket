using SupermarketSystem.API.Common;
using SupermarketSystem.Application.Branches.CreateBranch;
using SupermarketSystem.Application.Branches.GetBranches;
using SupermarketSystem.Application.Branches.SetBranchActive;
using SupermarketSystem.Application.Branches.UpdateBranch;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;

namespace SupermarketSystem.API.Endpoints;

/// <summary>
/// Minimal APIs rather than controllers: this is a thin transport layer —
/// deserialize, call the handler, map the Result to HTTP. There is no logic
/// here worth a controller's ceremony, and keeping it thin is what stops
/// business rules leaking into the API layer.
/// </summary>
public static class BranchEndpoints
{
    public static IEndpointRouteBuilder MapBranchEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/branches")
            .WithTags("Branches")
            .RequirePermission(PermissionCodes.BranchesManage);

        group.MapPost("/", async (
            CreateBranchCommand command,
            CreateBranchHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(command, cancellationToken);

            return result.ToHttpResult(response =>
                Results.Created($"/api/v1/branches/{response.BranchId}", response));
        })
        .WithName("CreateBranch")
        .WithSummary("Creates a branch and provisions its document number sequences.")
        .Produces<CreateBranchResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/", async (
            int? pageNumber,
            int? pageSize,
            string? search,
            string? sortBy,
            string? sortDirection,
            GetBranchesHandler handler,
            CancellationToken cancellationToken) =>
        {
            var paging = PagingBinder.Build(pageNumber, pageSize, search, sortBy, sortDirection);
            var result = await handler.HandleAsync(new GetBranchesQuery(paging), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetBranches")
        .Produces<PagedResult<BranchListItemDto>>(StatusCodes.Status200OK);

        group.MapPut("/{branchId:guid}", async (
            Guid branchId,
            UpdateBranchRequest request,
            UpdateBranchHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new UpdateBranchCommand(branchId, request.Name, request.PhoneNumber), cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("UpdateBranch")
        .WithSummary("يعدّل اسم/هاتف فرع موجود - الكود (Code) غير قابل للتعديل (مستخدَم بترقيم مستندات سابقة).")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{branchId:guid}/active", async (
            Guid branchId,
            SetBranchActiveRequest request,
            SetBranchActiveHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new SetBranchActiveCommand(branchId, request.IsActive), cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("SetBranchActive")
        .WithSummary("يفعّل/يوقف فرع - بلا حذف فعلي (راجع تعليق Branch.cs).")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    public sealed record UpdateBranchRequest(string Name, string? PhoneNumber);
    public sealed record SetBranchActiveRequest(bool IsActive);
}
