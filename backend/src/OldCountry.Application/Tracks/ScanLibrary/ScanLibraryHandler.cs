using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OldCountry.Application.Abstractions.Audio;
using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Application.Abstractions.Persistence;
using OldCountry.Application.Common.Library;
using OldCountry.Application.Common.Telemetry;
using OldCountry.Domain.Artists;
using OldCountry.Domain.Errors;
using OldCountry.Domain.Tracks;

namespace OldCountry.Application.Tracks.ScanLibrary;

internal sealed partial class ScanLibraryHandler(
    IAppDbContext db,
    ArtistDirectory artists,
    IAudioMetadataReader reader,
    IOptions<LibraryOptions> options,
    TimeProvider clock,
    AppMetrics metrics,
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
        if (LibraryOptions.Check(roots) is { } invalid)
        {
            metrics.ScanEnded("refused", 0);
            return invalid;
        }

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
                    // Keep the memory use flat in big libraries: saved tracks do not need tracking.
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
        metrics.ScanEnded("completed", report.Added);
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

    /// <summary>Tags can be longer than the library allows. Keep the start of the text and keep the file.</summary>
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
