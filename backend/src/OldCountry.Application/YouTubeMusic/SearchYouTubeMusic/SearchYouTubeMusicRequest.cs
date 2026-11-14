namespace OldCountry.Application.YouTubeMusic.SearchYouTubeMusic;

/// <summary>The query string of the search endpoint: <c>?query=...&amp;limit=20</c>.</summary>
/// <param name="Query">The text to search for.</param>
/// <param name="Limit">The number of results, 1-50.</param>
public sealed record SearchYouTubeMusicRequest(string? Query, int Limit = 20);
