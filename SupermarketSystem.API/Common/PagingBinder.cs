using SupermarketSystem.Application.Common.Pagination;

namespace SupermarketSystem.API.Common;

/// <summary>
/// Builds a PagedRequest from individually-bound nullable query parameters
/// instead of [AsParameters] over the record directly. [AsParameters] on
/// PagedRequest was returning an unconditional 400 with an empty body for
/// every request — even with every value supplied explicitly — which points
/// at a minimal-API binding quirk with complex-type [AsParameters] rather
/// than anything about property defaults. Plain nullable primitive query
/// parameters (int?, string?) are the most basic, reliably-supported
/// binding case in minimal APIs, so every paged endpoint binds those
/// directly and constructs PagedRequest here instead.
/// </summary>
public static class PagingBinder
{
    public static PagedRequest Build(int? pageNumber, int? pageSize, string? search, string? sortBy, string? sortDirection)
        => new PagedRequest
        {
            PageNumber = pageNumber ?? 1,
            PageSize = pageSize ?? 20,
            Search = search,
            SortBy = sortBy,
            SortDirection = sortDirection ?? "asc"
        }.Normalized();
}
