namespace OldCountry.Application.YouTubeMusic.SearchYouTubeMusic;

/// <summary>One search result, and whether it is already in the library.</summary>
/// <param name="VideoId">The YouTube video id. Use it to play, download or add the track.</param>
/// <param name="Title">The title of the video.</param>
/// <param name="Artist">The name of the artist or the channel, when it is known.</param>
/// <param name="Duration">The length, when it is known.</param>
/// <param name="ThumbnailUrl">The largest thumbnail, when it is known.</param>
/// <param name="TrackId">The library track that is linked to this video, if there is one.</param>
/// <param name="IsAvailableOffline">True when that library track has a local file.</param>
public sealed record YouTubeSearchResult(
    string VideoId,
    string Title,
    string? Artist,
    TimeSpan? Duration,
    string? ThumbnailUrl,
    Guid? TrackId,
    bool IsAvailableOffline);
