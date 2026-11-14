using OldCountry.Domain.Downloads;

namespace OldCountry.Application.Playlists;

/// <summary>One position of a playlist, with its track.</summary>
/// <param name="Position">The zero-based index. Use it to remove or move this entry.</param>
/// <param name="TrackId">The id of the track.</param>
/// <param name="Title">The title of the track.</param>
/// <param name="Artist">The name of the artist.</param>
/// <param name="Duration">The length of the track.</param>
/// <param name="IsAvailableOffline">True when the track has a local file.</param>
/// <param name="YouTubeVideoId">The YouTube Music video id, when the track is on YouTube. Clients use it to play tracks without a local file.</param>
/// <param name="AddedAt">When the track was added to this playlist.</param>
/// <param name="DownloadProblem">The reason that YouTube refused the last download of the track. It stays until a download succeeds.</param>
public sealed record PlaylistEntryResponse(
    int Position,
    Guid TrackId,
    string Title,
    string Artist,
    TimeSpan Duration,
    bool IsAvailableOffline,
    string? YouTubeVideoId,
    DateTimeOffset AddedAt,
    DownloadRefusal? DownloadProblem);
