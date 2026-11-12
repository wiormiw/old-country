using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using OldCountry.Api.Common.Errors;
using OldCountry.Api.Common.Library;
using OldCountry.Domain.Errors;
using OldCountry.Domain.Library;

namespace OldCountry.Api.Features.YouTubeMusic.StreamYouTubeAudio;

/// <summary>
/// Plays YouTube audio by letting yt-dlp fetch it into a size-capped cache, then serving the file.
/// </summary>
/// <remarks>
/// Relaying YouTube's stream URLs directly was tried first and abandoned (Nov 2026): a resolved URL
/// only works from the IP that resolved it, googlevideo.com rejects the Range header, and URLs for
/// some of yt-dlp's clients (e.g. TVHTML5) are refused outright unless fetched the way yt-dlp's own
/// downloader does. yt-dlp keeps up with those changes; this class just reuses it. A song takes a
/// few seconds the first time and is instant (and seekable) after that.
/// </remarks>
internal sealed partial class YouTubeAudioCache(IYtDlp ytDlp, IOptions<YouTubeMusicOptions> options, TimeProvider clock, ILogger<YouTubeAudioCache> logger)
{
    // One fetch per video at a time: a player opening several range requests at once waits for
    // the first fetch instead of starting its own.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _fetching = new(StringComparer.Ordinal);

    public string Folder => options.Value.StreamCacheFolder ?? Path.Combine(Path.GetTempPath(), "old-country", "stream-cache");

    public async Task<(string? Path, ExternalServiceError? Error)> GetAsync(YouTubeVideoId videoId, CancellationToken cancellationToken)
    {
        if (Find(videoId) is { } cached) return (Touch(cached), null);

        var gate = _fetching.GetOrAdd(videoId.Value, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (Find(videoId) is { } fetchedMeanwhile) return (Touch(fetchedMeanwhile), null);

            Directory.CreateDirectory(Folder);
            var path = await ytDlp.FetchAudioAsync(videoId, Folder, cancellationToken);
            var sizeBytes = new FileInfo(path).Length;
            Log.Cached(logger, videoId.Value, sizeBytes);
            Trim();
            return (path, null);
        }
        catch (YtDlpException exception)
        {
            return (null, exception.ToError());
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>A finished file for this video; yt-dlp's in-progress <c>.part</c> files don't count.</summary>
    private string? Find(YouTubeVideoId videoId) =>
        Directory.Exists(Folder)
            ? Directory.EnumerateFiles(Folder, $"{videoId.Value}.*").FirstOrDefault(f => !f.EndsWith(".part", StringComparison.Ordinal) && !f.EndsWith(".ytdl", StringComparison.Ordinal))
            : null;

    /// <summary>Marks a file as recently played, so trimming removes it last.</summary>
    private string Touch(string path)
    {
        File.SetLastAccessTimeUtc(path, clock.GetUtcNow().UtcDateTime);
        return path;
    }

    /// <summary>Deletes the least recently played files until the cache fits its size limit.</summary>
    private void Trim()
    {
        var limit = options.Value.StreamCacheMaxMegabytes * 1024L * 1024L;
        var files = new DirectoryInfo(Folder).GetFiles().OrderByDescending(f => f.LastAccessTimeUtc).ToList();
        long total = 0;
        foreach (var file in files)
        {
            total += file.Length;
            if (total <= limit) continue;
            try
            {
                file.Delete();
            }
            catch (IOException)
            {
                // Being served right now; it goes next time.
            }
        }
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 1310, Level = LogLevel.Information, Message = "Cached YouTube audio for {VideoId} ({SizeBytes} bytes)")]
        public static partial void Cached(ILogger logger, string videoId, long sizeBytes);
    }
}

internal static class StreamYouTubeAudioEndpoint
{
    public static void Map(IEndpointRouteBuilder youTube) => youTube
        .MapGet("/videos/{videoId}/audio", Handle)
        .WithName("StreamYouTubeAudio")
        .WithSummary("Play a YouTube video's audio")
        .WithDescription("The first request fetches the audio with yt-dlp (a few seconds); later ones, and range requests for seeking, are served from the cache. Prefer GET /api/tracks/{id}/audio when the track is available offline.")
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status502BadGateway);

    private static async Task<Results<PhysicalFileHttpResult, ValidationProblem, ProblemHttpResult>> Handle(
        string videoId, YouTubeAudioCache cache, CancellationToken cancellationToken)
    {
        if (YouTubeVideoId.Create(videoId).Value is not YouTubeVideoId id)
            return ValidationError.For("videoId", "A YouTube video id is 11 characters of letters, digits, '-' or '_'.").ToProblem();

        var (path, error) = await cache.GetAsync(id, cancellationToken);
        return path is not null
            ? TypedResults.PhysicalFile(path, ContentTypeOf(path), enableRangeProcessing: true)
            : error!.ToProblem();
    }

    /// <summary>YouTube's native containers first (WebM/Opus, M4A/AAC), then the library's formats.</summary>
    private static string ContentTypeOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".webm" => "audio/webm",
        _ => AudioFormats.ContentTypeOf(path),
    };
}
