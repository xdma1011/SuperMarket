namespace SupermarketSystem.Application.Common.Pagination;

/// <summary>
/// Shared shape for every list operation (brief §18: PageNumber, PageSize,
/// Search, SortBy, SortDirection). Offset pagination, not keyset — none of
/// the lists built in this slice are the "very large dataset" case §18
/// reserves keyset for; if one later is, that query's Handle method changes,
/// not this shared contract.
///
/// Deliberately has NO knowledge of HTTP/query-string binding — that would
/// require referencing ASP.NET Core types, which Application must not
/// depend on. Binding lives in the API layer
/// (SupermarketSystem.API.Common.PagingBinder) and constructs this type from
/// individually-bound query parameters; this record only ever deals in
/// already-parsed values.
/// </summary>
public sealed record PagedRequest
{
    private const int MaxPageSize = 100;
    private const int DefaultPageSize = 20;

    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = DefaultPageSize;
    public string? Search { get; init; }
    public string? SortBy { get; init; }
    public string SortDirection { get; init; } = "asc";

    /// <summary>
    /// Returns a copy with PageNumber/PageSize clamped to sane bounds.
    /// Called once, by PagingBinder.Build() at the API boundary, so every
    /// handler always receives an already-normalized instance.
    /// </summary>
    public PagedRequest Normalized() => this with
    {
        PageNumber = PageNumber < 1 ? 1 : PageNumber,
        PageSize = PageSize switch
        {
            < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => PageSize
        }
    };

    public bool IsDescending => string.Equals(SortDirection, "desc", StringComparison.OrdinalIgnoreCase);
    public int Skip => (PageNumber - 1) * PageSize;
}

/// <summary>
/// Every list handler returns this, never a bare list — TotalCount is what
/// lets a client render page controls without a second round trip.
/// </summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int PageNumber, int PageSize)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
