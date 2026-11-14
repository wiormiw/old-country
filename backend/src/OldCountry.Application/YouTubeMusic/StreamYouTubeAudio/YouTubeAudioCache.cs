using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using OldCountry.Application.Abstractions.YouTube;
using OldCountry.Application.Common.YouTube;
using OldCountry.Domain.Errors;
using OldCountry.Domain.YouTube;

namespace OldCountry.Application.YouTubeMusic.StreamYouTubeAudio;

/// <summary>
/// Plays YouTube audio: yt-dlp gets it into a cache with a size limit, and the server serves the file.
/// </summary>
/// <remarks>
/// A direct relay of the YouTube stream URLs was tried first and stopped (Nov 2026).
/// A resolved URL works only from the IP address that resolved it, and googlevideo.com refuses the Range header.
/// YouTube also refuses the URLs of some yt-dlp clients (for example TVHTML5), unless the downloader of yt-dlp gets them.
/// yt-dlp follows those changes, and this class uses it. A track takes a few seconds the first time.
/// After that it starts at once and you can seek in it.
/// </remarks>
public partial class YouTubeAudioCache(
    IYtDlp ytDlp,
    IOptions<YouTubeMusicOptions> options,
    TimeProvider clock,
    IHostApplicationLifetime lifetime,
    ILogger<YouTubeAudioCache> logger)
{
    // One fetch for each video. All requests for the video share it, because a player sends many range requests at once.
    // The fetch belongs to the server, not to a request. When the listener skips or disconnects, the fetch
    // still finishes and goes into the cache. Thus a return to the track is immediate.
    private readonly ConcurrentDictionary<string, Lazy<Task<string>>> _inFlight = new(StringComparer.Ordinal);

    public string Folder => options.Value.StreamCacheFolder ?? Path.Combine(Path.GetTempPath(), "old-country", "stream-cache");

    public async Task<(string? Path, ExternalServiceError? Error)> GetAsync(YouTubeVideoId videoId, CancellationToken cancellationToken)
    {
        if (Find(videoId) is { } cached) return (Touch(cached), null);

        var fetch = _inFlight.GetOrAdd(videoId.Value, _ => new Lazy<Task<string>>(() => FetchAsync(videoId)));
        try
        {
            // The token of the caller only stops the wait of this caller. It never stops the fetch.
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

    /// <summary>A finished file for this video. The <c>.part</c> files of yt-dlp downloads in progress do not count.</summary>
    private string? Find(YouTubeVideoId videoId) =>
        Directory.Exists(Folder)
            ? Directory.EnumerateFiles(Folder, $"{videoId.Value}.*").FirstOrDefault(f => !f.EndsWith(".part", StringComparison.Ordinal) && !f.EndsWith(".ytdl", StringComparison.Ordinal))
            : null;

    /// <summary>Marks a file as played recently, so the trim removes it last.</summary>
    private string Touch(string path)
    {
        File.SetLastAccessTimeUtc(path, clock.GetUtcNow().UtcDateTime);
        return path;
    }

    /// <summary>Deletes the files that played least recently, until the cache is in its size limit.</summary>
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
                // The server sends this file now. The next trim deletes it.
            }
        }
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 1310, Level = LogLevel.Information, Message = "Cached YouTube audio for {VideoId} ({SizeBytes} bytes)")]
        public static partial void Cached(ILogger logger, string videoId, long sizeBytes);
    }
}
