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
internal sealed partial class YouTubeAudioCache(
    IYtDlp ytDlp,
    IOptions<YouTubeMusicOptions> options,
    TimeProvider clock,
    IHostApplicationLifetime lifetime,
    ILogger<YouTubeAudioCache> logger)
{
    // One fetch per video, shared by everyone asking for it (a player opens several range requests
    // at once). The fetch belongs to the server, not to a request: if the listener skips ahead or
    // disconnects, it still finishes and is cached, so coming back to the song is instant.
    private readonly ConcurrentDictionary<string, Lazy<Task<string>>> _inFlight = new(StringComparer.Ordinal);

    public string Folder => options.Value.StreamCacheFolder ?? Path.Combine(Path.GetTempPath(), "old-country", "stream-cache");

    public async Task<(string? Path, ExternalServiceError? Error)> GetAsync(YouTubeVideoId videoId, CancellationToken cancellationToken)
    {
        if (Find(videoId) is { } cached) return (Touch(cached), null);

        var fetch = _inFlight.GetOrAdd(videoId.Value, _ => new Lazy<Task<string>>(() => FetchAsync(videoId)));
        try
        {
            // The caller's token only stops this caller waiting, never the fetch itself.
            return (await fetch.Value.WaitAsync(cancellationToken), null);
        }
        catch (YtDlpException exception)
        {
            return (null, exception.ToError());
        }
    }

    private async Task<string> FetchAsync(YouTubeVideoId videoId)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var path = await ytDlp.FetchAudioAsync(videoId, Folder, lifetime.ApplicationStopping);
            var sizeBytes = new FileInfo(path).Length;
            Log.Cached(logger, videoId.Value, sizeBytes);
            Trim();
            return path;
        }
        finally
        {
            _inFlight.TryRemove(videoId.Value, out _);
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
