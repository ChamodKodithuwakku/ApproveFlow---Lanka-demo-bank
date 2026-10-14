namespace ApproveFlow.Application.Common.Models;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < TotalPages;
}

public enum SortDirection
{
    Ascending = 0,
    Descending = 1,
}

/// <summary>Paging guard rails shared by UI and API queries.</summary>
public static class Paging
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public static (int Page, int PageSize) Normalise(int page, int pageSize) =>
        (Math.Max(1, page), pageSize <= 0 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize));
}
