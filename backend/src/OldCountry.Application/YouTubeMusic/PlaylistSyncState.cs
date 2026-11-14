using OldCountry.Domain.Playlists;

namespace OldCountry.Application.YouTubeMusic;

/// <summary>
/// The memory of the automatic sync between rounds. It is a singleton.
/// After a restart it is empty, and the sync simply tries each playlist again.
/// </summary>
public sealed class PlaylistSyncState
{
    /// <summary>When the last sync of a playlist failed. The sync skips that playlist for one interval.</summary>
    internal Dictionary<PlaylistId, DateTimeOffset> FailedAt { get; } = [];

    /// <summary>Lets only one round run at a time.</summary>
    internal SemaphoreSlim OneRound { get; } = new(1, 1);
}
