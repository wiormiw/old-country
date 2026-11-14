using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Domain.Playlists;

namespace OldCountry.Application.YouTubeMusic.SyncPlaylist;

/// <summary>
/// Syncs a library playlist with the YouTube Music playlist that it came from.
/// A sync is an import of the same YouTube playlist (see ImportPlaylist).
/// </summary>
[NoTransaction]
public sealed record SyncPlaylistCommand(PlaylistId Id) : ICommand<SyncPlaylistResult>;
