using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OldCountry.Api.Common.Errors;
using OldCountry.Api.Common.Messaging;
using OldCountry.Api.Features.YouTubeMusic.DownloadTracks;
using OldCountry.Api.Features.YouTubeMusic.ImportPlaylist;
using OldCountry.Api.Persistence;
using OldCountry.Domain.Common;
using OldCountry.Domain.Errors;

namespace OldCountry.Api.Features.YouTubeMusic.SyncPlaylists;

// Keeping imported playlists up to date with YouTube Music:
//
//   POST /playlists/{id}/sync   -> sync one playlist now (the app's Sync button)
//   PlaylistSyncScheduler       -> every 15 minutes, syncs each linked playlist whose last sync is
//                                  older than YouTubeMusic:PlaylistSyncInterval (6 h by default)
//
// A sync is an import of the same YouTube playlist: songs added on YouTube since are appended; the
// library playlist's name, description, order and removals stay as they are (see ImportPlaylist).

/// <summary>Syncs a library playlist with the YouTube Music playlist it was imported from.</summary>
[NoTransaction]
public sealed record SyncPlaylistCommand(PlaylistId Id) : ICommand<SyncPlaylistResult>;

public readonly union SyncPlaylistResult(ImportPlaylistReport, NotFoundError, ValidationError, ExternalServiceError);

internal sealed class SyncPlaylistHandler(
    OldCountryDbContext db,
    IOptions<YouTubeMusicOptions> options,
    ICommandHandler<ImportPlaylistCommand, ImportPlaylistResult> import) : ICommandHandler<SyncPlaylistCommand, SyncPlaylistResult>
{
    public async Task<SyncPlaylistResult> Handle(SyncPlaylistCommand command, CancellationToken cancellationToken)
    {
        var playlist = await db.Playlists.AsNoTracking()
            .Where(p => p.Id == command.Id)
            .Select(p => new { p.YouTubeListId })
            .SingleOrDefaultAsync(cancellationToken);
        if (playlist is null) return new NotFoundError("playlist", command.Id.ToString());
        if (playlist.YouTubeListId is not { } listId)
            return ValidationError.For("playlist", "This playlist was not imported from YouTube Music, so there is nothing to sync it with.");
        if (PlaylistSource.IsLikedList(listId) && options.Value.CookiesFile is null)
            return ValidationError.For("playlist", "Syncing liked songs needs your YouTube Music cookies: set YouTubeMusic:CookiesFile.");

        return await import.Handle(new ImportPlaylistCommand(PlaylistSource.FromListId(listId)), cancellationToken) switch
        {
            ImportPlaylistReport report => report,
            ExternalServiceError failed => failed,
        };
    }
}

/// <summary>
/// Syncs imported playlists on its own, so songs added on YouTube Music show up without a click.
/// Every <see cref="Tick"/> it syncs, one at a time, each linked playlist whose last sync is older
/// than the interval. Last-sync times are stored, so a restart neither skips nor repeats work.
/// It rests with the download queue when YouTube rate limits, and waits a full interval before it
/// tries a playlist again after a failure (for example, a playlist that was deleted on YouTube).
/// </summary>
internal sealed partial class PlaylistSyncScheduler(
    IServiceScopeFactory scopes,
    DownloadPauses pauses,
    TimeProvider clock,
    IOptions<YouTubeMusicOptions> options,
    ILogger<PlaylistSyncScheduler> logger) : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan StartDelay = TimeSpan.FromMinutes(1);

    private readonly Dictionary<PlaylistId, DateTimeOffset> _failedAt = [];
    private readonly SemaphoreSlim _oneRound = new(1, 1);

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
            await Task.Delay(StartDelay, clock, stoppingToken);   // let the app finish starting
            using var timer = new PeriodicTimer(Tick, clock);
            do
            {
                try
                {
                    await SyncDueAsync(interval, stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // Since .NET 11 an exception escaping a BackgroundService stops the host.
                    Log.RoundFailed(logger, exception);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>Syncs each linked playlist last synced before now - <paramref name="olderThan"/>. Returns how many synced.</summary>
    public async Task<int> SyncDueAsync(TimeSpan olderThan, CancellationToken cancellationToken)
    {
        await _oneRound.WaitAsync(cancellationToken);
        try
        {
            var now = clock.GetUtcNow();
            var cutOff = now - olderThan;
            List<PlaylistId> due;
            await using (var scope = scopes.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<OldCountryDbContext>();
                due = await db.Playlists.AsNoTracking()
                    .Where(p => p.YouTubeListId != null && (p.YouTubeSyncedAt == null || p.YouTubeSyncedAt < cutOff))
                    .OrderBy(p => p.YouTubeSyncedAt)
                    .Select(p => p.Id)
                    .ToListAsync(cancellationToken);
            }

            var synced = 0;
            foreach (var id in due)
            {
                if (_failedAt.TryGetValue(id, out var failedAt) && now - failedAt < olderThan) continue;
                if (pauses.Remaining(clock.GetUtcNow()) is { } rest)
                {
                    Log.Resting(logger, rest);
                    break;
                }

                await using var scope = scopes.CreateAsyncScope();
                var sync = scope.ServiceProvider.GetRequiredService<ICommandHandler<SyncPlaylistCommand, SyncPlaylistResult>>();
                switch (await sync.Handle(new SyncPlaylistCommand(id), cancellationToken))
                {
                    case ImportPlaylistReport report:
                        synced++;
                        _failedAt.Remove(id);
                        if (report.Added > 0) Log.Added(logger, report.Name, report.Added);
                        break;
                    case ExternalServiceError { Code: YouTubeRefusals.RateLimited } limited:
                        var until = pauses.RateLimited(clock.GetUtcNow(), options.Value.RateLimitPause, limited.Message);
                        Log.RateLimited(logger, until);
                        return synced;
                    case ExternalServiceError failed:
                        _failedAt[id] = clock.GetUtcNow();
                        Log.SyncFailed(logger, id.Value, failed.Message);
                        break;
                    case ValidationError:
                        _failedAt[id] = clock.GetUtcNow();   // e.g. liked songs without cookies: nothing to do until settings change
                        break;
                    case NotFoundError:
                        break;   // deleted meanwhile
                }
            }
            return synced;
        }
        finally
        {
            _oneRound.Release();
        }
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 1340, Level = LogLevel.Information, Message = "Automatic playlist sync is off (YouTubeMusic:PlaylistSyncInterval is 0)")]
        public static partial void Off(ILogger logger);

        [LoggerMessage(EventId = 1341, Level = LogLevel.Information, Message = "Synced \"{Playlist}\" from YouTube Music: {Added} new songs")]
        public static partial void Added(ILogger logger, string playlist, int added);

        [LoggerMessage(EventId = 1342, Level = LogLevel.Warning, Message = "Could not sync playlist {PlaylistId} from YouTube Music: {Reason}")]
        public static partial void SyncFailed(ILogger logger, Guid playlistId, string reason);

        [LoggerMessage(EventId = 1343, Level = LogLevel.Warning, Message = "YouTube is rate limiting; playlist sync waits until {Until}")]
        public static partial void RateLimited(ILogger logger, DateTimeOffset until);

        [LoggerMessage(EventId = 1344, Level = LogLevel.Debug, Message = "Playlist sync waits {Rest} while YouTube rate limits")]
        public static partial void Resting(ILogger logger, TimeSpan rest);

        [LoggerMessage(EventId = 1345, Level = LogLevel.Error, Message = "A playlist sync round failed; the next round tries again")]
        public static partial void RoundFailed(ILogger logger, Exception exception);
    }
}

internal static class SyncPlaylistEndpoint
{
    public static void Map(IEndpointRouteBuilder youTube) => youTube
        .MapPost("/playlists/{id:guid}/sync", Handle)
        .WithName("SyncYouTubePlaylist")
        .WithSummary("Sync an imported playlist with YouTube Music now")
        .WithDescription("Appends songs added on YouTube since the last sync; the playlist's name, description, order and removals "
            + "are kept. The server also does this on its own every YouTubeMusic:PlaylistSyncInterval.")
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status502BadGateway);

    private static async Task<Results<Ok<ImportPlaylistReport>, ProblemHttpResult, ValidationProblem>> Handle(
        Guid id, ICommandHandler<SyncPlaylistCommand, SyncPlaylistResult> handler, CancellationToken cancellationToken) =>
        await handler.Handle(new SyncPlaylistCommand(new PlaylistId(id)), cancellationToken) switch
        {
            ImportPlaylistReport report => TypedResults.Ok(report),
            NotFoundError missing => missing.ToProblem(),
            ValidationError invalid => invalid.ToProblem(),
            ExternalServiceError failed => failed.ToProblem(),
        };
}
