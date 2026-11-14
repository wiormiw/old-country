using OldCountry.Domain.YouTube;

namespace OldCountry.Application.Abstractions.YouTube;

/// <summary>A video as yt-dlp tells it, with only the values that the library needs.</summary>
public sealed record YtVideo(YouTubeVideoId Id, string Title, string? Artist, TimeSpan? Duration, string? ThumbnailUrl);
