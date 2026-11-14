using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Application.Abstractions.Persistence;
using OldCountry.Application.Common.Telemetry;
using OldCountry.Application.Common.YouTube;
using OldCountry.Application.YouTubeMusic.ImportPlaylist;
using OldCountry.Application.YouTubeMusic.SyncPlaylist;
using OldCountry.Domain.Errors;

namespace OldCountry.Application.YouTubeMusic.SyncDuePlaylists;

/// <summary>
/// Syncs the imported playlists that are due, so tracks added on YouTube Music arrive without a click.
/// The last sync times are in the database, so a restart does not skip or repeat work.
/// The sync rests with the download queue when YouTube limits the rate.
/// After a failure (for example, a playlist that YouTube deleted), the sync waits one full interval for that playlist.
/// </summary>
internal sealed partial class SyncDuePlaylistsHandler(
    IAppDbContext db,
    IServiceScopeFactory scopes,
    PlaylistSyncState state,
    YouTubeRateLimit rateLimit,
    TimeProvider clock,
    IOptions<YouTubeMusicOptions> options,
    AppMetrics metrics,
    ILogger<SyncDuePlaylistsHandler> logger) : ICommandHandler<SyncDuePlaylistsCommand, int>
{
    public async Task<int> Handle(SyncDuePlaylistsCommand command, CancellationToken cancellationToken)
    {
        await state.OneRound.WaitAsync(cancellationToken);
        try
        {
            var now = clock.GetUtcNow();
            var cutOff = now - command.OlderThan;
            var due = await db.Playlists.AsNoTracking()
                .Where(p => p.YouTubeListId != null && (p.YouTubeSyncedAt == null || p.YouTubeSyncedAt < cutOff))
                .OrderBy(p => p.YouTubeSyncedAt)
                .Select(p => p.Id)
                .ToListAsync(cancellationToken);

            var synced = 0;
            foreach (var id in due)
            {
                if (state.FailedAt.TryGetValue(id, out var failedAt) && now - failedAt < command.OlderThan) continue;
                if (rateLimit.Remaining(clock.GetUtcNow()) is { } rest)
                {
                    Log.Resting(logger, rest);
                    break;
                }

                // Each sync has its own scope, so that one sync cannot see the tracked entities of another.
                await using var scope = scopes.CreateAsyncScope();
                var sync = scope.ServiceProvider.GetRequiredService<ICommandHandler<SyncPlaylistCommand, SyncPlaylistResult>>();
                switch (await sync.Handle(new SyncPlaylistCommand(id), cancellationToken))
                {
                    case ImportPlaylistReport report:
                        synced++;
                        state.FailedAt.Remove(id);
                        metrics.PlaylistSynced("synced");
                        if (report.Added > 0) Log.Added(logger, report.Name, report.Added);
                        break;
                    case ExternalServiceError { Code: YouTubeRefusals.RateLimited } limited:
                        var until = rateLimit.RateLimited(clock.GetUtcNow(), options.Value.RateLimitPause, limited.Message);
                        Log.RateLimited(logger, until);
                        metrics.RateLimited("playlist_sync");
                        return synced;
                    case ExternalServiceError failed:
                        state.FailedAt[id] = clock.GetUtcNow();
                        Log.SyncFailed(logger, id.Value, failed.Message);
                        metrics.PlaylistSynced("failed");
                        break;
                    case ValidationError:
                        // For example, liked songs without cookies. Nothing changes until the settings change.
                        state.FailedAt[id] = clock.GetUtcNow();
                        break;
                    case NotFoundError:
                        // The playlist was deleted during the round.
                        break;
                }
            }
            return synced;
        }
        finally
        {
            state.OneRound.Release();
        }
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 1341, Level = LogLevel.Information, Message = "Synced \"{Playlist}\" from YouTube Music: {Added} new songs")]
        public static partial void Added(ILogger logger, string playlist, int added);

        [LoggerMessage(EventId = 1342, Level = LogLevel.Warning, Message = "Could not sync playlist {PlaylistId} from YouTube Music: {Reason}")]
        public static partial void SyncFailed(ILogger logger, Guid playlistId, string reason);

        [LoggerMessage(EventId = 1343, Level = LogLevel.Warning, Message = "YouTube is rate limiting; playlist sync waits until {Until}")]
        public static partial void RateLimited(ILogger logger, DateTimeOffset until);

        [LoggerMessage(EventId = 1344, Level = LogLevel.Debug, Message = "Playlist sync waits {Rest} while YouTube rate limits")]
        public static partial void Resting(ILogger logger, TimeSpan rest);
    }
}
