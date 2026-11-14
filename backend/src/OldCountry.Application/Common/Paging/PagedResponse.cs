namespace OldCountry.Application.Common.Paging;

/// <summary>One page of a list, with the values that a pager needs.</summary>
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (TotalCount + PageSize - 1) / PageSize;
}
