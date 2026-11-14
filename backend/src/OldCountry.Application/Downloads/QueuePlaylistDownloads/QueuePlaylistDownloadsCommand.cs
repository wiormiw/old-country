using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Application.Downloads.QueueDownloads;
using OldCountry.Domain.Playlists;

namespace OldCountry.Application.Downloads.QueuePlaylistDownloads;

/// <summary>Queues the downloads of a playlist. It has no outer transaction, for the same reason as <see cref="QueueDownloadsCommand"/>.</summary>
[NoTransaction]
public sealed record QueuePlaylistDownloadsCommand(PlaylistId PlaylistId, bool RetrySkipped) : ICommand<QueuePlaylistDownloadsResult>;
