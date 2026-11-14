namespace OldCountry.Application.Tracks.ListTracks;

/// <summary>The query string of the list endpoint: <c>?page=2&amp;pageSize=50&amp;search=dust</c>.</summary>
/// <param name="Page">The page number, from 1.</param>
/// <param name="PageSize">The number of tracks on each page, 1-200.</param>
/// <param name="Search">Optional text to find in titles and artist names, in any case.</param>
public sealed record ListTracksRequest(int Page = 1, int PageSize = 50, string? Search = null);
