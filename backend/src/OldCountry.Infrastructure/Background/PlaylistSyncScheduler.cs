using Microsoft.Extensions.Options;
using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Application.Common.YouTube;
using OldCountry.Application.YouTubeMusic.SyncDuePlaylists;

namespace OldCountry.Infrastructure.Background;

/// <summary>
/// Starts the automatic playlist sync at each <see cref="Tick"/>.
/// The work of one round is <see cref="SyncDuePlaylistsCommand"/>. This loop only waits and repeats.
/// </summary>
/// <remarks>
/// POST /playlists/{id}/sync syncs one playlist at once (the Sync button in the apps).
/// This loop syncs each linked playlist with a last sync older than <c>YouTubeMusic:PlaylistSyncInterval</c> (default 6 hours).
/// A sync is an import of the same YouTube playlist. It adds the new tracks only.
/// The name, the description, the order and the removals in the library playlist stay.
/// </remarks>
internal sealed partial class PlaylistSyncScheduler(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    IOptions<YouTubeMusicOptions> options,
    ILogger<PlaylistSyncScheduler> logger) : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan StartDelay = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = options.Value.PlaylistSyncInterval;
        if (interval == TimeSpan.Zero)
        {
            Log.Off(logger);
            return;
        }

        try
        {
            // Let the server complete its start first.
            await Task.Delay(StartDelay, clock, stoppingToken);
            using var timer = new PeriodicTimer(Tick, clock);
            do
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<SyncDuePlaylistsCommand, int>>();
                    await handler.Handle(new SyncDuePlaylistsCommand(interval), stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // Since .NET 11, an exception that leaves a BackgroundService stops the host.
                    Log.RoundFailed(logger, exception);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The server stops.
        }
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 1340, Level = LogLevel.Information, Message = "Automatic playlist sync is off (YouTubeMusic:PlaylistSyncInterval is 0)")]
        public static partial void Off(ILogger logger);

        [LoggerMessage(EventId = 1345, Level = LogLevel.Error, Message = "A playlist sync round failed; the next round tries again")]
        public static partial void RoundFailed(ILogger logger, Exception exception);
    }
}
