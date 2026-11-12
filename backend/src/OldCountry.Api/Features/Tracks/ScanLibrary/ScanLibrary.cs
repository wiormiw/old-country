using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OldCountry.Api.Common.Library;
using OldCountry.Api.Common.Errors;
using OldCountry.Api.Common.Messaging;
using OldCountry.Api.Persistence;
using OldCountry.Domain.Errors;
using OldCountry.Domain.Library;

namespace OldCountry.Api.Features.Tracks.ScanLibrary;

public enum ScanTrigger
{
    Startup,
    Scheduled,
    Manual,
}

/// <summary>
/// Walks the configured library folders and adds every new audio file as a track. Runs in the
/// background (see <see cref="LibraryScanner"/>), saving in batches, so it holds no transaction.
/// </summary>
[NoTransaction]
public sealed record ScanLibraryCommand(ScanTrigger Trigger) : ICommand<ScanLibraryResult>;

/// <summary>What one scan found.</summary>
/// <param name="Trigger">What started the scan.</param>
/// <param name="FilesSeen">Audio files found under the library folders.</param>
/// <param name="Added">New tracks added to the library.</param>
/// <param name="AlreadyKnown">Files that were already in the library.</param>
/// <param name="Unreadable">Files skipped because their audio or tags could not be read.</param>
/// <param name="Missing">Library tracks whose file is no longer on disk. They are kept, not deleted.</param>
/// <param name="StartedAt">When the scan started.</param>
/// <param name="FinishedAt">When the scan finished.</param>
public sealed record ScanReport(
    ScanTrigger Trigger,
    int FilesSeen,
    int Added,
    int AlreadyKnown,
    int Unreadable,
    int Missing,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt);

public readonly union ScanLibraryResult(ScanReport, ValidationError);

internal sealed partial class ScanLibraryHandler(
    OldCountryDbContext db,
    ArtistDirectory artists,
    IAudioMetadataReader reader,
    IOptions<LibraryOptions> options,
    TimeProvider clock,
    ILogger<ScanLibraryHandler> logger) : ICommandHandler<ScanLibraryCommand, ScanLibraryResult>
{
    private const int BatchSize = 100;
    private const string UnknownArtist = "Unknown Artist";

    private static readonly EnumerationOptions Walk = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
    };

    public async Task<ScanLibraryResult> Handle(ScanLibraryCommand command, CancellationToken cancellationToken)
    {
        var roots = options.Value.Roots;
        if (LibraryOptions.Check(roots) is { } invalid) return invalid;

        var startedAt = clock.GetUtcNow();
        Log.ScanStarted(logger, command.Trigger, roots.Count);

        var known = await db.Tracks
            .Where(track => track.LocalFile != null)
            .Select(track => track.LocalFile!.Path)
            .ToHashSetAsync(StringComparer.Ordinal, cancellationToken);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int filesSeen = 0, added = 0, alreadyKnown = 0, unreadable = 0, pending = 0;

        foreach (var root in roots)
        {
            foreach (var path in Directory.EnumerateFiles(root, "*", Walk))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!AudioFormats.TryGetCodec(path, out _)) continue;

                filesSeen++;
                seen.Add(path);
                if (known.Contains(path))
                {
                    alreadyKnown++;
                    continue;
                }

                if (await TryCreateTrack(path, cancellationToken) is not { } track)
                {
                    unreadable++;
                    continue;
                }

                db.Tracks.Add(track);
                added++;
                if (++pending >= BatchSize)
                {
                    await db.SaveChangesAsync(cancellationToken);
                    // Keep memory flat on big libraries: saved tracks needn't stay tracked.
                    db.ChangeTracker.Clear();
                    pending = 0;
                }
            }
        }

        if (pending > 0) await db.SaveChangesAsync(cancellationToken);

        var missing = known.Count(path => !seen.Contains(path) && roots.Any(root => path.StartsWith(root, StringComparison.Ordinal)));
        var report = new ScanReport(command.Trigger, filesSeen, added, alreadyKnown, unreadable, missing, startedAt, clock.GetUtcNow());
        Log.ScanFinished(logger, report.Trigger, report.Added, report.AlreadyKnown, report.Unreadable, report.Missing,
            (report.FinishedAt - report.StartedAt).TotalSeconds);
        return report;
    }

    private async Task<Track?> TryCreateTrack(string path, CancellationToken cancellationToken)
    {
        if (reader.Read(path) is not { } metadata)
        {
            Log.Unreadable(logger, path);
            return null;
        }

        var size = new FileInfo(path).Length;
        var quality = new AudioQuality(metadata.Codec, metadata.BitrateKbps, metadata.SampleRateHz);
        if (LocalFile.Create(path, size, quality) is not LocalFile file)
        {
            Log.Unreadable(logger, path);
            return null;
        }

        var artist = await artists.FindOrCreateAsync(Clip(metadata.Artist ?? UnknownArtist, Artist.NameMaxLength), cancellationToken);
        var title = Clip(metadata.Title ?? Path.GetFileNameWithoutExtension(path), Track.TitleMaxLength);

        return Track.Create(title, artist.Id, metadata.Duration, file, youTubeVideoId: null) switch
        {
            Track track => track,
            ValidationError => null,
        };
    }

    /// <summary>Tags can be longer than the library allows; keep the start rather than lose the file.</summary>
    private static string Clip(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];

    private static partial class Log
    {
        [LoggerMessage(EventId = 1100, Level = LogLevel.Information, Message = "Library scan ({Trigger}) started over {RootCount} folder(s)")]
        public static partial void ScanStarted(ILogger logger, ScanTrigger trigger, int rootCount);

        [LoggerMessage(EventId = 1101, Level = LogLevel.Information,
            Message = "Library scan ({Trigger}) finished in {ElapsedSeconds:0.0} s: {Added} added, {AlreadyKnown} known, {Unreadable} unreadable, {Missing} missing")]
        public static partial void ScanFinished(ILogger logger, ScanTrigger trigger, int added, int alreadyKnown, int unreadable, int missing, double elapsedSeconds);

        [LoggerMessage(EventId = 1102, Level = LogLevel.Warning, Message = "Skipped unreadable audio file {Path}")]
        public static partial void Unreadable(ILogger logger, string path);
    }
}

/// <summary>Whether a scan is running, and what the last one found.</summary>
/// <param name="IsRunning">A scan is in progress right now.</param>
/// <param name="IsQueued">A scan is waiting to start.</param>
/// <param name="LastScan">The most recent finished scan since the server started, if any.</param>
public sealed record ScanStatusResponse(bool IsRunning, bool IsQueued, ScanReport? LastScan);

internal static class ScanLibraryEndpoints
{
    public const string StatusName = "GetLibraryScanStatus";

    public static void Map(IEndpointRouteBuilder tracks)
    {
        tracks.MapPost("/scans", RequestScan)
            .WithName("ScanLibrary")
            .WithSummary("Scan the library folders for new audio files")
            .WithDescription("Queues a scan and returns at once. A scan already waiting absorbs this request. Poll the status endpoint for the result.");

        tracks.MapGet("/scans/latest", GetStatus)
            .WithName(StatusName)
            .WithSummary("Library scan status and the last scan's report");
    }

    private static Results<AcceptedAtRoute<ScanStatusResponse>, ValidationProblem> RequestScan(
        LibraryScanQueue queue, LibraryScanStatus status, IOptions<LibraryOptions> options)
    {
        if (LibraryOptions.Check(options.Value.Roots) is { } invalid) return invalid.ToProblem();

        queue.Request(ScanTrigger.Manual);
        return TypedResults.AcceptedAtRoute(status.Snapshot(queue), StatusName);
    }

    private static Ok<ScanStatusResponse> GetStatus(LibraryScanQueue queue, LibraryScanStatus status) =>
        TypedResults.Ok(status.Snapshot(queue));
}
