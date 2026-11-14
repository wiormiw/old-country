using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Domain.YouTube;

namespace OldCountry.Application.Downloads.QueueDownloads;

/// <summary>
/// Queues downloads of the given videos. It has no outer transaction: its one save is atomic.
/// Also, the worker must wake only after the commit. Otherwise it looks, finds no jobs, and sleeps.
/// </summary>
[NoTransaction]
public sealed record QueueDownloadsCommand(IReadOnlyList<YouTubeVideoId> VideoIds) : ICommand<QueueDownloadsResult>;
