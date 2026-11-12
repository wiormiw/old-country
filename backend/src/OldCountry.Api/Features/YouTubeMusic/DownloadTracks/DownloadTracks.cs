using System.Diagnostics;
using System.Threading.Channels;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OldCountry.Api.Common.Errors;
using OldCountry.Api.Common.Library;
using OldCountry.Api.Common.Messaging;
using OldCountry.Api.Common.Validation;
using OldCountry.Api.Persistence;
using OldCountry.Domain.Common;
using OldCountry.Domain.Errors;
using OldCountry.Domain.Library;

namespace OldCountry.Api.Features.YouTubeMusic.DownloadTracks;

// Downloads, end to end:
//
//   POST /downloads, POST /playlists/{id}/downloads
//     -> rows in download_jobs (state Queued), then ring the doorbell (a Channel)
//   DownloadWorker (one BackgroundService)
//     -> wakes on the doorbell, or every 30 s regardless
//     -> claims the oldest claimable job: SELECT ... FOR UPDATE SKIP LOCKED, then Start(lease)
//     -> runs DownloadTrackHandler (yt-dlp into the library folder, track saved as offline)
//     -> marks the job Completed / Failed, or Postponed and the queue paused when rate limited
//     -> waits YouTubeMusic:DownloadPause, then the next job
//   GET /downloads -> recent jobs, and whether the queue is paused
//
// The jobs live in Postgres, so a restart loses nothing: queued jobs stay queued, and a job that
// was running when the process died is taken again once its lease runs out.

/// <summary>Videos to download into the library for offline play.</summary>
public sealed record DownloadTracksRequest
{
    /// <summary>YouTube video ids, 1-50 per request.</summary>
    public required IReadOnlyList<string> VideoIds { get; init; }
}

internal sealed class DownloadTracksRequestValidator : AbstractValidator<DownloadTracksRequest>
{
    public const int MaxPerRequest = 50;

    public DownloadTracksRequestValidator()
    {
        RuleFor(r => r.VideoIds)
            .NotEmpty().WithMessage("Pick at least one video to download.")
            .Must(ids => ids.Count <= MaxPerRequest).WithMessage($"Download at most {MaxPerRequest} videos at a time.");
        RuleForEach(r => r.VideoIds)
            .Must(id => YouTubeVideoId.Create(id).Value is YouTubeVideoId)
            .WithMessage("'{PropertyValue}' is not a YouTube video id (11 characters of letters, digits, '-' or '_').");
    }
}

/// <summary>Download every song of a playlist that isn't downloaded yet.</summary>
public sealed record PlaylistDownloadRequest
{
    /// <summary>
    /// Also retry songs YouTube refused for a lasting reason (Premium only, private, removed,
    /// age or region restricted), e.g. after getting Premium or adding cookies. Off by default.
    /// </summary>
    public bool RetrySkipped { get; init; }
}

public enum DownloadState
{
    Queued,
    Running,
    Completed,
    Failed,
}

/// <summary>Where one download stands.</summary>
/// <param name="JobId">Id of this download.</param>
/// <param name="VideoId">The YouTube video being downloaded.</param>
/// <param name="Title">The song's title, when it is a library track.</param>
/// <param name="State">Queued, Running, Completed or Failed.</param>
/// <param name="TrackId">The library track, once completed.</param>
/// <param name="ErrorCode">Why it failed, as a stable code (<c>youtube.premium_only</c>, <c>youtube.private</c>, ...).</param>
/// <param name="Error">Why it failed, in words.</param>
/// <param name="Attempts">How many times it has been started.</param>
/// <param name="QueuedAt">When it was requested.</param>
/// <param name="FinishedAt">When it completed or failed.</param>
public sealed record DownloadStatus(
    Guid JobId,
    string VideoId,
    string? Title,
    DownloadState State,
    Guid? TrackId,
    string? ErrorCode,
    string? Error,
    int Attempts,
    DateTimeOffset QueuedAt,
    DateTimeOffset? FinishedAt);

/// <summary>Recent downloads, and whether the queue is resting.</summary>
/// <param name="Jobs">The most recent downloads, newest first.</param>
/// <param name="PausedUntil">When YouTube asked us to slow down: when downloading resumes.</param>
/// <param name="PauseReason">What YouTube said.</param>
public sealed record DownloadsResponse(IReadOnlyList<DownloadStatus> Jobs, DateTimeOffset? PausedUntil, string? PauseReason);

/// <summary>What asking for a playlist's downloads did.</summary>
/// <param name="Queued">Songs queued now.</param>
/// <param name="AlreadyDownloaded">Songs already playable offline.</param>
/// <param name="AlreadyQueued">Songs already waiting or downloading.</param>
/// <param name="Skipped">Songs left out because YouTube refused them before for a lasting reason; retry with <c>retrySkipped</c>.</param>
/// <param name="NotOnYouTube">Songs with no YouTube video (local files only).</param>
public sealed record PlaylistDownloadReport(int Queued, int AlreadyDownloaded, int AlreadyQueued, int Skipped, int NotOnYouTube);

internal static class DownloadStatuses
{
    public static DownloadStatus ToStatus(this DownloadJob job, string? title) => new(
        job.Id.Value,
        job.VideoId.Value,
        title,
        job.State switch
        {
            DownloadJobState.Queued => DownloadState.Queued,
            DownloadJobState.Running => DownloadState.Running,
            DownloadJobState.Completed => DownloadState.Completed,
            _ => DownloadState.Failed,
        },
        job.TrackId?.Value,
        job.ErrorCode,
        job.Error,
        job.Attempts,
        job.QueuedAt,
        job.FinishedAt);

    /// <summary>Titles of the library tracks behind these videos.</summary>
    public static async Task<Dictionary<YouTubeVideoId, string>> TitlesAsync(
        this OldCountryDbContext db, IEnumerable<YouTubeVideoId> videos, CancellationToken cancellationToken)
    {
        var ids = videos.Select(v => (YouTubeVideoId?)v).Distinct().ToList();
        return await db.Tracks
            .Where(t => ids.Contains(t.YouTubeVideoId))
            .Select(t => new { Video = t.YouTubeVideoId!.Value, t.Title })
            .ToDictionaryAsync(t => t.Video, t => t.Title, cancellationToken);
    }

    /// <summary>
    /// A job per video, joining the job already queued or running for a video rather than
    /// starting a second one. Saves, then wakes the worker.
    /// </summary>
    public static async Task<List<DownloadJob>> QueueAsync(
        this OldCountryDbContext db, IReadOnlyCollection<YouTubeVideoId> videos, DownloadDoorbell doorbell, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var active = await db.DownloadJobs
            .Where(j => videos.Contains(j.VideoId) && (j.State == DownloadJobState.Queued || j.State == DownloadJobState.Running))
            .ToDictionaryAsync(j => j.VideoId, cancellationToken);
        var jobs = new List<DownloadJob>(videos.Count);
        foreach (var video in videos.Distinct())
        {
            if (!active.TryGetValue(video, out var job))
            {
                job = DownloadJob.Queue(video, now);
                db.DownloadJobs.Add(job);
            }
            jobs.Add(job);
        }
        await db.SaveChangesAsync(cancellationToken);
        doorbell.Ring();   // after the commit (callers run without an outer transaction), so the worker sees the jobs
        return jobs;
    }
}

/// <summary>
/// Wakes the download worker when jobs are queued: a Channel holding at most one ring, so many
/// requests in a row still wake it once. The jobs themselves are in the database; this only says
/// "look now" instead of waiting for the worker's next periodic check.
/// </summary>
internal sealed class DownloadDoorbell
{
    private readonly Channel<bool> _rings = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    public void Ring() => _rings.Writer.TryWrite(true);

    /// <summary>Returns when rung, or after <paramref name="timeout"/>.</summary>
    public async Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        wait.CancelAfter(timeout);
        try
        {
            await _rings.Reader.ReadAsync(wait.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The timeout: time for the periodic check.
        }
    }
}

/// <summary>
/// The queue's rest after YouTube says to slow down. Each rate limit in a row doubles the rest
/// (up to 8x <see cref="YouTubeMusicOptions.RateLimitPause"/>); a successful download resets it.
/// Kept in memory: after a restart the queue simply tries again.
/// </summary>
internal sealed class DownloadPauses
{
    private readonly Lock _lock = new();
    private int _inARow;

    public DateTimeOffset? Until { get; private set; }

    public string? Reason { get; private set; }

    public DateTimeOffset RateLimited(DateTimeOffset now, TimeSpan basePause, string reason)
    {
        lock (_lock)
        {
            _inARow = Math.Min(_inARow + 1, 4);
            Until = now + basePause * (1 << (_inARow - 1));
            Reason = reason;
            return Until.Value;
        }
    }

    public void Succeeded()
    {
        lock (_lock)
        {
            _inARow = 0;
            Until = null;
            Reason = null;
        }
    }

    public TimeSpan? Remaining(DateTimeOffset now) => Until is { } until && until > now ? until - now : null;
}

// --- Queue downloads of chosen videos ---

/// <summary>
/// No surrounding transaction: its one SaveChanges is atomic on its own, and the worker must be
/// woken only after the jobs are committed, or it looks, finds nothing yet, and sleeps.
/// </summary>
[NoTransaction]
public sealed record QueueDownloadsCommand(IReadOnlyList<YouTubeVideoId> VideoIds) : ICommand<QueueDownloadsResult>;

public readonly union QueueDownloadsResult(IReadOnlyList<DownloadStatus>, ValidationError);

internal sealed class QueueDownloadsHandler(
    OldCountryDbContext db,
    DownloadDoorbell doorbell,
    TimeProvider clock,
    IOptions<YouTubeMusicOptions> youTube,
    IOptions<LibraryOptions> library) : ICommandHandler<QueueDownloadsCommand, QueueDownloadsResult>
{
    public async Task<QueueDownloadsResult> Handle(QueueDownloadsCommand command, CancellationToken cancellationToken)
    {
        // Refuse up front if there is nowhere to put the files, rather than failing every job.
        if (youTube.Value.ResolveDownloadFolder(library.Value).Value is ValidationError invalid) return invalid;

        var jobs = await db.QueueAsync(command.VideoIds, doorbell, clock.GetUtcNow(), cancellationToken);
        var titles = await db.TitlesAsync(command.VideoIds, cancellationToken);
        IReadOnlyList<DownloadStatus> statuses = [.. jobs.Select(job => job.ToStatus(titles.GetValueOrDefault(job.VideoId)))];
        return statuses;
    }
}

// --- Queue a whole playlist ---

/// <summary>No surrounding transaction, for the same reason as <see cref="QueueDownloadsCommand"/>.</summary>
[NoTransaction]
public sealed record QueuePlaylistDownloadsCommand(PlaylistId PlaylistId, bool RetrySkipped) : ICommand<QueuePlaylistDownloadsResult>;

public readonly union QueuePlaylistDownloadsResult(PlaylistDownloadReport, NotFoundError, ValidationError);

internal sealed class QueuePlaylistDownloadsHandler(
    OldCountryDbContext db,
    DownloadDoorbell doorbell,
    TimeProvider clock,
    IOptions<YouTubeMusicOptions> youTube,
    IOptions<LibraryOptions> library) : ICommandHandler<QueuePlaylistDownloadsCommand, QueuePlaylistDownloadsResult>
{
    public async Task<QueuePlaylistDownloadsResult> Handle(QueuePlaylistDownloadsCommand command, CancellationToken cancellationToken)
    {
        var playlist = await db.Playlists.AsNoTracking().SingleOrDefaultAsync(p => p.Id == command.PlaylistId, cancellationToken);
        if (playlist is null) return new NotFoundError("playlist", command.PlaylistId.ToString());
        if (youTube.Value.ResolveDownloadFolder(library.Value).Value is ValidationError invalid) return invalid;

        // Each song once, in playlist order.
        var trackIds = playlist.Entries.Select(entry => entry.TrackId).Distinct().ToList();
        var tracks = await db.Tracks.AsNoTracking().Where(t => trackIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, cancellationToken);
        int downloaded = 0, skipped = 0, notOnYouTube = 0;
        var wanted = new List<YouTubeVideoId>();
        foreach (var track in trackIds.Select(tracks.GetValueOrDefault).OfType<Track>())
        {
            if (track.IsAvailableOffline) downloaded++;
            else if (track.YouTubeVideoId is not { } video) notOnYouTube++;
            else if (track.DownloadProblem is { IsLasting: true } && !command.RetrySkipped) skipped++;
            else wanted.Add(video);
        }

        var alreadyQueued = await db.DownloadJobs
            .Where(j => wanted.Contains(j.VideoId) && (j.State == DownloadJobState.Queued || j.State == DownloadJobState.Running))
            .CountAsync(cancellationToken);
        if (wanted.Count > 0) await db.QueueAsync(wanted, doorbell, clock.GetUtcNow(), cancellationToken);

        return new PlaylistDownloadReport(wanted.Count - alreadyQueued, downloaded, alreadyQueued, skipped, notOnYouTube);
    }
}

// --- List ---

public sealed record ListDownloadsQuery : IQuery<DownloadsResponse>;

internal sealed class ListDownloadsHandler(OldCountryDbContext db, DownloadPauses pauses, TimeProvider clock)
    : IQueryHandler<ListDownloadsQuery, DownloadsResponse>
{
    private const int Shown = 200;

    public async Task<DownloadsResponse> Handle(ListDownloadsQuery query, CancellationToken cancellationToken)
    {
        var jobs = await db.DownloadJobs.AsNoTracking().OrderByDescending(j => j.QueuedAt).Take(Shown).ToListAsync(cancellationToken);
        var titles = await db.TitlesAsync(jobs.Select(j => j.VideoId), cancellationToken);
        var paused = pauses.Remaining(clock.GetUtcNow()) is not null;
        return new DownloadsResponse(
            [.. jobs.Select(job => job.ToStatus(titles.GetValueOrDefault(job.VideoId)))],
            paused ? pauses.Until : null,
            paused ? pauses.Reason : null);
    }
}

// --- One download (run by the worker) ---

/// <summary>Downloads one video and records it as a track playable offline.</summary>
[NoTransaction]
public sealed record DownloadTrackCommand(YouTubeVideoId VideoId) : ICommand<DownloadTrackResult>;

public readonly union DownloadTrackResult(TrackId, ValidationError, ExternalServiceError);

internal sealed partial class DownloadTrackHandler(
    IYtDlp ytDlp,
    OldCountryDbContext db,
    ArtistDirectory artists,
    TimeProvider clock,
    IOptions<YouTubeMusicOptions> youTube,
    IOptions<LibraryOptions> library,
    ILogger<DownloadTrackHandler> logger) : ICommandHandler<DownloadTrackCommand, DownloadTrackResult>
{
    private const string UnknownArtist = "Unknown Artist";

    public async Task<DownloadTrackResult> Handle(DownloadTrackCommand command, CancellationToken cancellationToken)
    {
        var resolved = youTube.Value.ResolveDownloadFolder(library.Value);
        if (resolved is ValidationError noFolder) return noFolder;
        var folder = (string)resolved.Value!;

        var existing = await db.Tracks.SingleOrDefaultAsync(t => t.YouTubeVideoId == command.VideoId, cancellationToken);
        if (existing is { IsAvailableOffline: true }) return existing.Id;

        YtDownload download;
        try
        {
            download = await ytDlp.DownloadAudioAsync(command.VideoId, folder, cancellationToken);
        }
        catch (YtDlpException exception)
        {
            // Remember why on the track, so the app can show it and "download all" can skip it.
            if (existing is not null && YouTubeRefusals.RefusalOf(exception.Code) is { } refusal)
            {
                existing.RecordDownloadProblem(DownloadProblem.Of(refusal, exception.Message, clock.GetUtcNow()));
                await db.SaveChangesAsync(cancellationToken);
            }
            return exception.ToError();
        }

        var quality = new AudioQuality(YtDlpJson.CodecOf(download.Path), download.BitrateKbps, download.SampleRateHz);
        if (LocalFile.Create(download.Path, new FileInfo(download.Path).Length, quality).Value is not LocalFile file)
            return new ExternalServiceError("youtube.bad_download", "yt-dlp reported a file that can't be added to the library.");

        Track track;
        if (existing is not null)
        {
            existing.AttachLocalFile(file);
            track = existing;
        }
        else
        {
            var video = download.Video;
            var artist = await artists.FindOrCreateAsync(Clip(video.Artist ?? UnknownArtist, Artist.NameMaxLength), cancellationToken);
            var created = Track.Create(Clip(video.Title, Track.TitleMaxLength), artist.Id, video.Duration ?? TimeSpan.Zero, file, command.VideoId);
            if (created is ValidationError invalid) return invalid;
            track = (Track)created.Value!;
            db.Tracks.Add(track);
        }

        await db.SaveChangesAsync(cancellationToken);
        Log.Downloaded(logger, command.VideoId.Value, track.Id.Value);
        return track.Id;
    }

    private static string Clip(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];

    private static partial class Log
    {
        [LoggerMessage(EventId = 1320, Level = LogLevel.Information, Message = "Downloaded {VideoId} as track {TrackId}")]
        public static partial void Downloaded(ILogger logger, string videoId, Guid trackId);
    }
}

/// <summary>Takes queued downloads from the database one at a time and runs them.</summary>
internal sealed partial class DownloadWorker(
    IServiceScopeFactory scopes,
    DownloadDoorbell doorbell,
    DownloadPauses pauses,
    TimeProvider clock,
    IOptions<YouTubeMusicOptions> options,
    ILogger<DownloadWorker> logger) : BackgroundService
{
    /// <summary>Even without a ring, look again this often: for jobs whose lease ran out, or queued by another server.</summary>
    private static readonly TimeSpan IdleCheck = TimeSpan.FromSeconds(30);

    /// <summary>Finished jobs are forgotten after this long.</summary>
    private static readonly TimeSpan History = TimeSpan.FromDays(30);

    /// <summary>A download quicker than this didn't reach YouTube (already downloaded); no pause needed after it.</summary>
    private static readonly TimeSpan Instant = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (pauses.Remaining(clock.GetUtcNow()) is { } rest)
                {
                    await Task.Delay(rest, clock, stoppingToken);
                    continue;
                }

                var job = await ClaimAsync(stoppingToken);
                if (job is null)
                {
                    await ForgetOldJobsAsync(stoppingToken);
                    await doorbell.WaitAsync(IdleCheck, stoppingToken);
                    continue;
                }

                var took = Stopwatch.StartNew();
                await RunAsync(job, stoppingToken);
                if (took.Elapsed > Instant && options.Value.DownloadPause > TimeSpan.Zero)
                    await Task.Delay(options.Value.DownloadPause, clock, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // The database went away, say. Since .NET 11 an exception escaping a
                // BackgroundService stops the host, so log it and try again shortly.
                Log.LoopFailed(logger, exception);
                await Task.Delay(TimeSpan.FromSeconds(10), clock, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
    }

    /// <summary>
    /// Takes the oldest job that's queued, or running under a lease that has run out (its worker
    /// died). <c>FOR UPDATE SKIP LOCKED</c> makes this safe with more than one worker or server:
    /// each takes a different job, and none waits for another's lock.
    /// </summary>
    private async Task<DownloadJob?> ClaimAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OldCountryDbContext>();
        while (true)
        {
            var now = clock.GetUtcNow();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var candidates = await db.DownloadJobs
                .FromSql($"""
                    SELECT * FROM download_jobs
                    WHERE state = 'Queued' OR (state = 'Running' AND lease_until <= {now})
                    ORDER BY queued_at
                    LIMIT 1
                    FOR UPDATE SKIP LOCKED
                    """)
                .ToListAsync(cancellationToken);
            if (candidates is not [var job]) return null;

            var reclaimed = job.State == DownloadJobState.Running;
            var started = job.Start(now, options.Value.DownloadLease);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            if (reclaimed) Log.Reclaimed(logger, job.VideoId.Value, job.Attempts);
            if (started) return job;
            Log.GaveUp(logger, job.VideoId.Value);   // started too often without finishing; take the next
        }
    }

    private async Task RunAsync(DownloadJob job, CancellationToken stoppingToken)
    {
        DownloadTrackResult? result = null;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<DownloadTrackCommand, DownloadTrackResult>>();
            result = await handler.Handle(new DownloadTrackCommand(job.VideoId), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down: put it back now rather than leave it to its lease.
            await FinishAsync(job.Id, j => j.Postpone(), CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            Log.Crashed(logger, exception, job.VideoId.Value);
        }

        var now = clock.GetUtcNow();
        switch (result)
        {
            case TrackId trackId:
                pauses.Succeeded();
                await FinishAsync(job.Id, j => j.Complete(trackId, now), stoppingToken);
                break;
            case ExternalServiceError { Code: YouTubeRefusals.RateLimited } limited:
                var until = pauses.RateLimited(now, options.Value.RateLimitPause, limited.Message);
                Log.RateLimited(logger, until, limited.Message);
                await FinishAsync(job.Id, j => j.Postpone(), stoppingToken);
                break;
            case ExternalServiceError failed:
                await FinishAsync(job.Id, j => j.Fail(failed.Code, failed.Message, now), stoppingToken);
                break;
            case ValidationError invalid:
                await FinishAsync(job.Id, j => j.Fail("download.invalid", string.Join(" ", invalid.Errors.Values.SelectMany(m => m)), now), stoppingToken);
                break;
            default: // the handler threw
                await FinishAsync(job.Id, j => j.Fail("download.crashed", "The download failed unexpectedly; see the server log.", now), stoppingToken);
                break;
        }
    }

    private async Task FinishAsync(DownloadJobId id, Action<DownloadJob> change, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OldCountryDbContext>();
        if (await db.DownloadJobs.FindAsync([id], cancellationToken) is not { } job) return;
        change(job);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task ForgetOldJobsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OldCountryDbContext>();
        var before = clock.GetUtcNow() - History;
        await db.DownloadJobs
            .Where(j => (j.State == DownloadJobState.Completed || j.State == DownloadJobState.Failed) && j.FinishedAt < before)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 1321, Level = LogLevel.Error, Message = "Download of {VideoId} crashed")]
        public static partial void Crashed(ILogger logger, Exception exception, string videoId);

        [LoggerMessage(EventId = 1322, Level = LogLevel.Warning,
            Message = "YouTube is rate limiting downloads; the queue rests until {Until}: {Reason}")]
        public static partial void RateLimited(ILogger logger, DateTimeOffset until, string reason);

        [LoggerMessage(EventId = 1323, Level = LogLevel.Warning,
            Message = "Took over the download of {VideoId}: its lease ran out without it finishing (attempt {Attempts})")]
        public static partial void Reclaimed(ILogger logger, string videoId, int attempts);

        [LoggerMessage(EventId = 1324, Level = LogLevel.Error, Message = "Gave up on the download of {VideoId}: it never finished in 3 tries")]
        public static partial void GaveUp(ILogger logger, string videoId);

        [LoggerMessage(EventId = 1325, Level = LogLevel.Error, Message = "The download worker hit an error; trying again in 10 s")]
        public static partial void LoopFailed(ILogger logger, Exception exception);
    }
}

internal static class DownloadTracksEndpoints
{
    public const string ListName = "ListYouTubeDownloads";

    public static void Map(IEndpointRouteBuilder youTube)
    {
        youTube.MapPost("/downloads", Queue)
            .Validate<DownloadTracksRequest>()
            .WithName("DownloadYouTubeTracks")
            .WithSummary("Download videos' audio into the library for offline play")
            .WithDescription("Queues the downloads and returns at once; poll the downloads list for progress. "
                + "A video already queued or downloading joins that job instead of starting another.");

        youTube.MapPost("/playlists/{id:guid}/downloads", QueuePlaylist)
            .WithName("DownloadYouTubePlaylist")
            .WithSummary("Download every song of a playlist that isn't downloaded yet")
            .WithDescription("Songs YouTube refused before for a lasting reason (Premium only, private, removed, age or region) "
                + "are skipped unless retrySkipped is true.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        youTube.MapGet("/downloads", List)
            .WithName(ListName)
            .WithSummary("Recent downloads and where each stands, newest first, and whether the queue is resting");
    }

    private static async Task<Results<AcceptedAtRoute<IReadOnlyList<DownloadStatus>>, ValidationProblem>> Queue(
        DownloadTracksRequest request, ICommandHandler<QueueDownloadsCommand, QueueDownloadsResult> handler, CancellationToken cancellationToken)
    {
        var videos = request.VideoIds.Select(id => new YouTubeVideoId(id)).ToList();
        return await handler.Handle(new QueueDownloadsCommand(videos), cancellationToken) switch
        {
            IReadOnlyList<DownloadStatus> jobs => TypedResults.AcceptedAtRoute(jobs, ListName),
            ValidationError invalid => invalid.ToProblem(),
        };
    }

    private static async Task<Results<AcceptedAtRoute<PlaylistDownloadReport>, ProblemHttpResult, ValidationProblem>> QueuePlaylist(
        Guid id, PlaylistDownloadRequest? request, ICommandHandler<QueuePlaylistDownloadsCommand, QueuePlaylistDownloadsResult> handler,
        CancellationToken cancellationToken) =>
        await handler.Handle(new QueuePlaylistDownloadsCommand(new PlaylistId(id), request?.RetrySkipped ?? false), cancellationToken) switch
        {
            PlaylistDownloadReport report => TypedResults.AcceptedAtRoute(report, ListName),
            NotFoundError missing => missing.ToProblem(),
            ValidationError invalid => invalid.ToProblem(),
        };

    private static async Task<Ok<DownloadsResponse>> List(IQueryHandler<ListDownloadsQuery, DownloadsResponse> handler, CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.Handle(new ListDownloadsQuery(), cancellationToken));
}
