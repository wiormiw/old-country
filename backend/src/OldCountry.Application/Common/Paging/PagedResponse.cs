namespace OldCountry.Application.Common.Paging;

/// <summary>One page of a list, with the values that a pager needs.</summary>
/// <param name="Items">The items on this page.</param>
/// <param name="Page">The page number, from 1.</param>
/// <param name="PageSize">The largest number of items on a page.</param>
/// <param name="TotalCount">
/// The number of items on all pages. It is null when the client did not ask for it
/// (<c>includeTotal=false</c>): a client that reads all pages in a row needs the total only one time.
/// </param>
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int? TotalCount)
{
    public int? TotalPages => TotalCount is { } total && PageSize > 0 ? (total + PageSize - 1) / PageSize : null;
}
