using SupermarketSystem.API.Common;
using SupermarketSystem.Application.Branches.CreateBranch;
using SupermarketSystem.Application.Branches.GetBranches;
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

        // NOTE: no authorization is applied yet — authentication is not
        // implemented (PlaceholderCurrentUserContext). Every endpoint added
        // from here must gain .RequireAuthorization(...) with the appropriate
        // permission before this system is exposed beyond local development.

        return app;
    }
}
