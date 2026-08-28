using SupermarketSystem.API.Common;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Application.Users.CreateUser;
using SupermarketSystem.Application.Users.UpdateUser;
using SupermarketSystem.Application.Users.GetRoles;
using SupermarketSystem.Application.Users.GetUsers;

namespace SupermarketSystem.API.Endpoints;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/users")
            .WithTags("Users")
            .RequirePermission(PermissionCodes.UsersManage);

        group.MapPost("/", async (
            CreateUserCommand command,
            CreateUserHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(command, cancellationToken);
            return result.ToHttpResult(response => Results.Created($"/api/v1/users/{response.UserId}", response));
        })
        .WithName("CreateUser")
        .WithSummary("ينشئ مستخدمًا جديدًا، يجزّئ كلمة سره، ويربطه بدور وفرع.")
        .Produces<CreateUserResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/", async (
            int? pageNumber, int? pageSize, string? search, string? sortBy, string? sortDirection,
            GetUsersHandler handler,
            CancellationToken cancellationToken) =>
        {
            var paging = PagingBinder.Build(pageNumber, pageSize, search, sortBy, sortDirection);
            var result = await handler.HandleAsync(new GetUsersQuery(paging), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetUsers")
        .Produces<PagedResult<UserItemDto>>(StatusCodes.Status200OK);

        group.MapPut("/{userId:guid}", async (
            Guid userId,
            UpdateUserRequest request,
            UpdateUserHandler handler,
            CancellationToken cancellationToken) =>
        {
            var command = new UpdateUserCommand(
                userId, request.FullName, request.Email, request.RoleId, request.BranchId, request.IsActive);
            var result = await handler.HandleAsync(command, cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("UpdateUser")
        .WithSummary("يعدّل بروفايل المستخدم ودوره وفرعه الافتراضي وحالة تفعيله - يبطل كاش صلاحياته فورًا.")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/roles", async (
            GetRolesHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetRoles")
        .Produces<IReadOnlyList<RoleItemDto>>(StatusCodes.Status200OK);

        return app;
    }

    public sealed record UpdateUserRequest(string FullName, string Email, Guid RoleId, Guid BranchId, bool IsActive);
}
