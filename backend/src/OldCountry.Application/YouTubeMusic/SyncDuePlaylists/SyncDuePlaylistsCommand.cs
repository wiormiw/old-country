using OldCountry.Application.Abstractions.Messaging;

namespace OldCountry.Application.YouTubeMusic.SyncDuePlaylists;

/// <summary>
/// Syncs each linked playlist with a last sync before now minus <paramref name="OlderThan"/>, one at a time.
/// The result is the number of playlists that synced. The sync scheduler sends it at each tick.
/// </summary>
[NoTransaction]
public sealed record SyncDuePlaylistsCommand(TimeSpan OlderThan) : ICommand<int>;
