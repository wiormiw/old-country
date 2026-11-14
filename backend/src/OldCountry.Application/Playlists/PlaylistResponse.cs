namespace OldCountry.Application.Playlists;

/// <summary>A playlist with its tracks in order.</summary>
/// <param name="Id">The id of the playlist.</param>
/// <param name="Name">The name of the playlist.</param>
/// <param name="Description">An optional description.</param>
/// <param name="Entries">The tracks, in the play order.</param>
/// <param name="TotalDuration">The sum of the durations of the entries.</param>
/// <param name="CreatedAt">When the playlist was made.</param>
/// <param name="UpdatedAt">When it changed the last time, if it changed.</param>
/// <param name="YouTubeListId">The YouTube Music playlist that it came from and syncs with, if there is one.</param>
/// <param name="YouTubeSyncedAt">When it synced with that playlist the last time.</param>
public sealed record PlaylistResponse(
    Guid Id,
    string Name,
    string? Description,
    IReadOnlyList<PlaylistEntryResponse> Entries,
    TimeSpan TotalDuration,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    string? YouTubeListId,
    DateTimeOffset? YouTubeSyncedAt);
