using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;
using OldCountry.Application.Abstractions.YouTube;
using OldCountry.Application.Common.Telemetry;
using OldCountry.Application.Common.YouTube;
using OldCountry.Domain.YouTube;

namespace OldCountry.Infrastructure.YouTube;

/// <summary>Runs yt-dlp as a child process. The arguments go through ArgumentList: no shell, and no quote errors.</summary>
internal sealed partial class YtDlpProcess(IOptions<YouTubeMusicOptions> options, AppMetrics metrics, ILogger<YtDlpProcess> logger) : IYtDlp
{
    private YouTubeMusicOptions Settings => options.Value;

    public static Uri WatchUrl(YouTubeVideoId id) => new($"https://music.youtube.com/watch?v={id.Value}");

    public async Task<IReadOnlyList<YtVideo>> SearchAsync(string query, int limit, CancellationToken cancellationToken)
    {
        // Use the YouTube search, not the music.youtube.com search. In flat mode the YouTube Music search
        // returns only the id and the title. The YouTube search also returns the channel, the duration and the thumbnails
        // (checked with yt-dlp 2026.10). The video ids are the same on both, so the results play and download the same.
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
            // Print the final info (with the file path after post-processing) as JSON, when the file is in its place.
            "--no-simulate", "-O", "after_move:%()j",
            WatchUrl(videoId).ToString(),
        ], cancellationToken);
        return YtDlpJson.ParseDownload(output);
    }

    /// <summary>
    /// Runs yt-dlp. When it fails without a stated reason, it tries one more time after a short pause.
    /// YouTube sometimes refuses requests (an old player script, a dropped connection), and a second try usually works.
    /// It does not try again for a missing program, a timeout, or a refusal with a reason (Premium only, private, rate limit).
    /// A second try would not change the answer.
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

    /// <summary>The trace source of the yt-dlp runs. ServiceDefaults exports it.</summary>
    public const string ActivitySourceName = "OldCountry.Infrastructure.YtDlp";

    private static readonly ActivitySource Tracing = new(ActivitySourceName);

    /// <summary>
    /// One run of yt-dlp, with a trace span and a duration measurement.
    /// Thus a slow search or a slow first play shows the time of yt-dlp in the trace.
    /// </summary>
    private async Task<string> RunOnceAsync(string operation, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        using var activity = Tracing.StartActivity($"yt-dlp {operation}", ActivityKind.Client);
        activity?.SetTag("oldcountry.ytdlp.operation", operation);
        var started = Stopwatch.GetTimestamp();
        try
        {
            var output = await RunProcessAsync(operation, arguments, cancellationToken);
            metrics.YtDlpRan(operation, "ok", null, Stopwatch.GetElapsedTime(started));
            return output;
        }
        catch (YtDlpException exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Code);
            activity?.SetTag("oldcountry.error.code", exception.Code);
            metrics.YtDlpRan(operation, "failed", exception.Code, Stopwatch.GetElapsedTime(started));
            throw;
        }
    }

    private async Task<string> RunProcessAsync(string operation, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
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
            // Replace the default runtime list of yt-dlp (Deno only) with the runtime from the settings.
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
