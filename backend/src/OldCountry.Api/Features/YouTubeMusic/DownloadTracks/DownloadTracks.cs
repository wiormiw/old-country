using System.Collections.Concurrent;
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
/// <param name="State">Queued, Running, Completed or Failed.</param>
/// <param name="TrackId">The library track, once completed.</param>
/// <param name="Error">Why it failed, when it did.</param>
/// <param name="QueuedAt">When it was requested.</param>
/// <param name="FinishedAt">When it completed or failed.</param>
public sealed record DownloadStatus(
    Guid JobId,
    string VideoId,
    DownloadState State,
    Guid? TrackId,
    string? Error,
    DateTimeOffset QueuedAt,
    DateTimeOffset? FinishedAt);

/// <summary>
/// Downloads: queued by the API, run one at a time by <see cref="DownloadWorker"/>, and
/// remembered in memory (the most recent ones) so the app can show progress.
/// </summary>
internal sealed class DownloadQueue(TimeProvider clock)
{
    private const int Remembered = 200;
    private readonly Channel<DownloadStatus> _pending = Channel.CreateUnbounded<DownloadStatus>(new UnboundedChannelOptions { SingleReader = true });
    private readonly ConcurrentDictionary<Guid, DownloadStatus> _jobs = new();

    public ChannelReader<DownloadStatus> Pending => _pending.Reader;

    public DownloadStatus Enqueue(YouTubeVideoId videoId)
    {
        var job = new DownloadStatus(Guid.CreateVersion7(), videoId.Value, DownloadState.Queued, null, null, clock.GetUtcNow(), null);
        _jobs[job.JobId] = job;
        _pending.Writer.TryWrite(job);
        Forget();
        return job;
    }

    public void Update(DownloadStatus status) => _jobs[status.JobId] = status;

    public DownloadStatus Finish(DownloadStatus job, Guid? trackId, string? error)
    {
        var done = job with
        {
            State = error is null ? DownloadState.Completed : DownloadState.Failed,
            TrackId = trackId,
            Error = error,
            FinishedAt = clock.GetUtcNow(),
        };
        _jobs[job.JobId] = done;
        return done;
    }

    /// <summary>Newest first.</summary>
    public IReadOnlyList<DownloadStatus> Recent() => [.. _jobs.Values.OrderByDescending(j => j.QueuedAt)];

    private void Forget()
    {
        if (_jobs.Count <= Remembered) return;
        foreach (var old in _jobs.Values.Where(j => j.State is DownloadState.Completed or DownloadState.Failed)
                     .OrderBy(j => j.QueuedAt).Take(_jobs.Count - Remembered))
            _jobs.TryRemove(old.JobId, out _);
    }
}

/// <summary>Downloads one video and records it as a track playable offline.</summary>
[NoTransaction]
public sealed record DownloadTrackCommand(YouTubeVideoId VideoId) : ICommand<DownloadTrackResult>;

public readonly union DownloadTrackResult(TrackId, ValidationError, ExternalServiceError);

internal sealed partial class DownloadTrackHandler(
    IYtDlp ytDlp,
    OldCountryDbContext db,
    ArtistDirectory artists,
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

/// <summary>Runs queued downloads one after another, each in its own DI scope.</summary>
internal sealed partial class DownloadWorker(IServiceScopeFactory scopes, DownloadQueue queue, ILogger<DownloadWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var job in queue.Pending.ReadAllAsync(stoppingToken))
                await RunAsync(job, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown; queued jobs are lost with the process, like the rest of this in-memory queue.
        }
    }

    private async Task RunAsync(DownloadStatus job, CancellationToken stoppingToken)
    {
        queue.Update(job with { State = DownloadState.Running });
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<DownloadTrackCommand, DownloadTrackResult>>();
            var result = await handler.Handle(new DownloadTrackCommand(new YouTubeVideoId(job.VideoId)), stoppingToken);
            _ = result switch
            {
                TrackId trackId => queue.Finish(job, trackId.Value, error: null),
                ValidationError invalid => queue.Finish(job, null, string.Join(" ", invalid.Errors.Values.SelectMany(m => m))),
                ExternalServiceError failed => queue.Finish(job, null, failed.Message),
            };
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Since .NET 11 an exception escaping a BackgroundService stops the host.
            Log.Crashed(logger, exception, job.VideoId);
            queue.Finish(job, null, "The download failed unexpectedly; see the server log.");
        }
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 1321, Level = LogLevel.Error, Message = "Download of {VideoId} crashed")]
        public static partial void Crashed(ILogger logger, Exception exception, string videoId);
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
            .WithDescription("Queues the downloads and returns at once; poll the downloads list for progress.");

        youTube.MapGet("/downloads", List)
            .WithName(ListName)
            .WithSummary("Recent downloads and where each stands, newest first");
    }

    private static Results<AcceptedAtRoute<IReadOnlyList<DownloadStatus>>, ValidationProblem> Queue(
        DownloadTracksRequest request, DownloadQueue queue, IOptions<YouTubeMusicOptions> youTube, IOptions<LibraryOptions> library)
    {
        // Refuse up front if there is nowhere to put the files, rather than failing every job.
        if (youTube.Value.ResolveDownloadFolder(library.Value).Value is ValidationError invalid) return invalid.ToProblem();

        IReadOnlyList<DownloadStatus> jobs = [.. request.VideoIds.Distinct(StringComparer.Ordinal).Select(id => queue.Enqueue(new YouTubeVideoId(id)))];
        return TypedResults.AcceptedAtRoute(jobs, ListName);
    }

    private static Ok<IReadOnlyList<DownloadStatus>> List(DownloadQueue queue) => TypedResults.Ok(queue.Recent());
}
