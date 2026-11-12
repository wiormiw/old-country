using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OldCountry.Api.Common.Library;
using OldCountry.Domain.Errors;
using OldCountry.Domain.Library;

namespace OldCountry.Api.Features.YouTubeMusic;

/// <summary>A video as yt-dlp describes it, reduced to what the library needs.</summary>
internal sealed record YtVideo(YouTubeVideoId Id, string Title, string? Artist, TimeSpan? Duration, string? ThumbnailUrl);

internal sealed record YtPlaylist(string Title, IReadOnlyList<YtVideo> Entries);

/// <summary>A finished download: where the file is and what was downloaded.</summary>
internal sealed record YtDownload(string Path, YtVideo Video, int BitrateKbps, int SampleRateHz);

/// <summary>yt-dlp failed, timed out, or isn't installed. <see cref="Code"/> is safe to show clients.</summary>
internal sealed class YtDlpException(string code, string message, Exception? inner = null) : Exception(message, inner)
{
    public string Code { get; } = code;

    public ExternalServiceError ToError() => new(Code, Message);
}

/// <summary>
/// Everything old-country asks of YouTube, done through the yt-dlp program. An interface so the
/// use cases can be tested without the network or the binary.
/// </summary>
internal interface IYtDlp
{
    Task<IReadOnlyList<YtVideo>> SearchAsync(string query, int limit, CancellationToken cancellationToken);

    /// <summary>Fetches the best audio stream as-is (no conversion) to <c>{folder}/{id}.{ext}</c>; returns the path.</summary>
    Task<string> FetchAudioAsync(YouTubeVideoId videoId, string folder, CancellationToken cancellationToken);

    Task<YtPlaylist> GetPlaylistAsync(Uri playlistUrl, CancellationToken cancellationToken);

    Task<YtDownload> DownloadAudioAsync(YouTubeVideoId videoId, string folder, CancellationToken cancellationToken);
}

/// <summary>Runs yt-dlp as a child process. Arguments go through ArgumentList: no shell, no quoting bugs.</summary>
internal sealed partial class YtDlpProcess(IOptions<YouTubeMusicOptions> options, ILogger<YtDlpProcess> logger) : IYtDlp
{
    private YouTubeMusicOptions Settings => options.Value;

    public static Uri WatchUrl(YouTubeVideoId id) => new($"https://music.youtube.com/watch?v={id.Value}");

    public async Task<IReadOnlyList<YtVideo>> SearchAsync(string query, int limit, CancellationToken cancellationToken)
    {
        // YouTube search, not music.youtube.com's: in flat mode YouTube Music's search returns only
        // id and title, while YouTube's includes channel, duration and thumbnails (checked against
        // yt-dlp 2026.10). The video ids are the same on both, so results stream and download alike.
        var json = await RunAsync("search", ["--flat-playlist", "-J", $"ytsearch{limit}:{query}"], cancellationToken);
        return YtDlpJson.ParsePlaylist(json).Entries;
    }

    public async Task<string> FetchAudioAsync(YouTubeVideoId videoId, string folder, CancellationToken cancellationToken)
    {
        var output = await RunAsync("fetch",
        [
            "-f", "bestaudio", "--no-playlist", "--no-progress",
            "-o", Path.Combine(folder, "%(id)s.%(ext)s"),
            "--no-simulate", "-O", "after_move:filepath",
            WatchUrl(videoId).ToString(),
        ], cancellationToken);
        var path = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
        return path is not null && Path.IsPathFullyQualified(path) && File.Exists(path)
            ? path
            : throw new YtDlpException("youtube.bad_output", "yt-dlp did not report where it saved the audio.");
    }

    public async Task<YtPlaylist> GetPlaylistAsync(Uri playlistUrl, CancellationToken cancellationToken)
    {
        var json = await RunAsync("playlist", ["--flat-playlist", "-J", playlistUrl.ToString()], cancellationToken);
        return YtDlpJson.ParsePlaylist(json);
    }

    public async Task<YtDownload> DownloadAudioAsync(YouTubeVideoId videoId, string folder, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(folder);
        var template = Path.Combine(folder, "%(artist,uploader)s", "%(title)s [%(id)s].%(ext)s");
        var output = await RunAsync("download",
        [
            "-f", "bestaudio", "-x", "--audio-format", Settings.AudioFormat, "--audio-quality", "0",
            "--embed-metadata", "--no-playlist", "--no-overwrites", "--no-progress",
            "-o", template,
            // Print the final info (with the post-processed file path) as JSON once the file is in place.
            "--no-simulate", "-O", "after_move:%()j",
            WatchUrl(videoId).ToString(),
        ], cancellationToken);
        return YtDlpJson.ParseDownload(output);
    }

    /// <summary>
    /// Runs yt-dlp, retrying once after a short pause when it fails for no stated reason: YouTube
    /// refuses requests now and then (a stale player script, a dropped connection) and a second
    /// try usually works. A missing binary, a timeout, or a refusal with a reason (Premium only,
    /// private, rate limited...) isn't retried: trying again wouldn't change the answer.
    /// </summary>
    private async Task<string> RunAsync(string operation, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        try
        {
            return await RunOnceAsync(operation, arguments, cancellationToken);
        }
        catch (YtDlpException exception) when (exception.Code == YouTubeRefusals.Failed)
        {
            Log.Retrying(logger, operation, exception.Message);
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            return await RunOnceAsync(operation, arguments, cancellationToken);
        }
    }

    private async Task<string> RunOnceAsync(string operation, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(Settings.YtDlpPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in (string[])["--no-warnings", "--ignore-config"]) start.ArgumentList.Add(argument);
        if (Settings.JsRuntime is { Length: > 0 } runtime)
        {
            // Replace yt-dlp's default runtime list (Deno only) with the configured one.
            start.ArgumentList.Add("--no-js-runtimes");
            start.ArgumentList.Add("--js-runtimes");
            start.ArgumentList.Add(runtime);
        }
        if (Settings.CookiesFile is { } cookies)
        {
            start.ArgumentList.Add("--cookies");
            start.ArgumentList.Add(cookies);
        }
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Settings.CommandTimeout);

        Process? process;
        try
        {
            process = Process.Start(start);
        }
        catch (Win32Exception exception)
        {
            throw new YtDlpException("youtube.ytdlp_missing",
                $"yt-dlp could not be started ('{Settings.YtDlpPath}'). Install it or set YouTubeMusic:YtDlpPath.", exception);
        }
        if (process is null) throw new YtDlpException("youtube.ytdlp_missing", "yt-dlp could not be started.");

        using (process)
        {
            var started = Stopwatch.GetTimestamp();
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                if (cancellationToken.IsCancellationRequested) throw;
                Log.TimedOut(logger, operation, Settings.CommandTimeout.TotalSeconds);
                throw new YtDlpException("youtube.timeout", $"yt-dlp took longer than {Settings.CommandTimeout} and was stopped.");
            }

            var output = await stdout;
            var error = await stderr;
            if (process.ExitCode != 0)
            {
                var reason = YtDlpJson.SummarizeError(error);
                Log.Failed(logger, operation, process.ExitCode, reason);
                throw new YtDlpException(YouTubeRefusals.Classify(reason), reason);
            }

            var elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            Log.Ran(logger, operation, elapsedMs);
            return output;
        }
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 1300, Level = LogLevel.Debug, Message = "yt-dlp {Operation} finished in {ElapsedMs:0} ms")]
        public static partial void Ran(ILogger logger, string operation, double elapsedMs);

        [LoggerMessage(EventId = 1301, Level = LogLevel.Warning, Message = "yt-dlp {Operation} exited with {ExitCode}: {Reason}")]
        public static partial void Failed(ILogger logger, string operation, int exitCode, string reason);

        [LoggerMessage(EventId = 1302, Level = LogLevel.Warning, Message = "yt-dlp {Operation} timed out after {TimeoutSeconds:0} s")]
        public static partial void TimedOut(ILogger logger, string operation, double timeoutSeconds);

        [LoggerMessage(EventId = 1303, Level = LogLevel.Information, Message = "yt-dlp {Operation} failed ({Reason}); retrying once")]
        public static partial void Retrying(ILogger logger, string operation, string reason);
    }
}

/// <summary>Reads yt-dlp's JSON output. Kept apart from the process so it can be tested with sample output.</summary>
internal static class YtDlpJson
{
    public static YtPlaylist ParsePlaylist(string json)
    {
        using var document = Parse(json);
        var root = document.RootElement;
        var entries = new List<YtVideo>();
        if (root.TryGetProperty("entries", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in list.EnumerateArray())
            {
                if (ParseVideo(entry) is { } video) entries.Add(video);
            }
        }
        return new YtPlaylist(String(root, "title") ?? "YouTube Music playlist", entries);
    }

    public static YtDownload ParseDownload(string output)
    {
        // With -O the JSON is the last non-empty line; anything before it is yt-dlp chatter.
        var line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault()
            ?? throw new YtDlpException("youtube.bad_output", "yt-dlp finished without describing the download.");
        using var document = Parse(line);
        var root = document.RootElement;

        var video = ParseVideo(root) ?? throw new YtDlpException("youtube.bad_output", "yt-dlp described the download without a valid video id.");
        var path = String(root, "filepath");
        if (path is null && root.TryGetProperty("requested_downloads", out var downloads) && downloads.ValueKind == JsonValueKind.Array)
            path = downloads.EnumerateArray().Select(d => String(d, "filepath")).LastOrDefault(p => p is not null);
        if (path is null || !Path.IsPathFullyQualified(path))
            throw new YtDlpException("youtube.bad_output", "yt-dlp did not report where it saved the file.");

        return new YtDownload(path, video, (int)Math.Round(Number(root, "abr") ?? 0), (int)(Number(root, "asr") ?? 0));
    }

    public static YtVideo? ParseVideo(JsonElement element)
    {
        if (YouTubeVideoId.Create(String(element, "id")).Value is not YouTubeVideoId id) return null;
        var title = String(element, "track") ?? String(element, "title");
        if (title is null) return null;

        var duration = Number(element, "duration") is { } seconds and > 0 ? TimeSpan.FromSeconds(Math.Round(seconds)) : (TimeSpan?)null;
        return new YtVideo(id, title, ArtistOf(element), duration, ThumbnailOf(element));
    }

    /// <summary>Music-specific fields first; a channel name with YouTube's " - Topic" suffix last.</summary>
    public static string? ArtistOf(JsonElement element)
    {
        if (element.TryGetProperty("artists", out var artists) && artists.ValueKind == JsonValueKind.Array
            && artists.EnumerateArray().Select(a => a.ValueKind == JsonValueKind.String ? a.GetString() : null).FirstOrDefault(a => !string.IsNullOrWhiteSpace(a)) is { } first)
            return first.Trim();
        return String(element, "artist") ?? String(element, "creator") ?? CleanChannel(String(element, "uploader") ?? String(element, "channel"));
    }

    public static string? CleanChannel(string? channel) =>
        channel is null ? null
        : channel.EndsWith(" - Topic", StringComparison.Ordinal) ? channel[..^" - Topic".Length].Trim()
        : channel;

    /// <summary>yt-dlp's last "ERROR:" line, without the prefix; that's the reason, the rest is noise.</summary>
    public static string SummarizeError(string stderr)
    {
        var lines = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var error = lines.LastOrDefault(l => l.StartsWith("ERROR:", StringComparison.Ordinal)) ?? lines.LastOrDefault() ?? "yt-dlp failed without saying why.";
        error = error.StartsWith("ERROR:", StringComparison.Ordinal) ? error["ERROR:".Length..].Trim() : error;
        return error.Length <= 500 ? error : error[..500];
    }

    private static string? ThumbnailOf(JsonElement element)
    {
        if (element.TryGetProperty("thumbnails", out var thumbnails) && thumbnails.ValueKind == JsonValueKind.Array)
        {
            // yt-dlp lists thumbnails smallest first.
            var best = thumbnails.EnumerateArray().Select(t => String(t, "url")).LastOrDefault(u => u is not null);
            if (best is not null) return best;
        }
        return String(element, "thumbnail");
    }

    private static JsonDocument Parse(string json)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new YtDlpException("youtube.bad_output", "yt-dlp returned output that isn't JSON.", exception);
        }
    }

    private static string? String(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;

    private static double? Number(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;

    /// <summary>The codec a downloaded file ended up as, from its extension.</summary>
    public static AudioCodec CodecOf(string path) => AudioFormats.TryGetCodec(path, out var codec) ? codec : AudioCodec.Unknown;
}
