using System.Diagnostics.Metrics;

namespace OldCountry.Application.Common.Telemetry;

/// <summary>
/// The metrics of old-country itself, beside the metrics of ASP.NET Core, EF Core and the runtime.
/// The dashboards and the alert rules in <c>deploy/observability</c> use these names. Do not rename them.
/// </summary>
public sealed class AppMetrics
{
    /// <summary>The meter name. ServiceDefaults exports it.</summary>
    public const string MeterName = "OldCountry.Application";

    private readonly Histogram<double> _useCaseDuration;
    private readonly Counter<long> _downloads;
    private readonly Counter<long> _rateLimits;
    private readonly Counter<long> _scans;
    private readonly Counter<long> _tracksAdded;
    private readonly Counter<long> _playlistSyncs;
    private readonly Histogram<double> _ytDlpDuration;

    public AppMetrics(IMeterFactory meters)
    {
        var meter = meters.Create(MeterName);
        _useCaseDuration = meter.CreateHistogram<double>(
            "oldcountry.use_case.duration", "s", "The duration of each use case (command or query), with its outcome.",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = [0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30, 60, 300] });
        _downloads = meter.CreateCounter<long>("oldcountry.downloads", "{download}", "Download jobs that ended, by outcome.");
        _rateLimits = meter.CreateCounter<long>("oldcountry.youtube.rate_limits", "{rate_limit}", "The times that YouTube asked the server to slow down.");
        _scans = meter.CreateCounter<long>("oldcountry.library.scans", "{scan}", "Library scans that ended, by outcome.");
        _tracksAdded = meter.CreateCounter<long>("oldcountry.library.tracks_added", "{track}", "Tracks that library scans added.");
        _playlistSyncs = meter.CreateCounter<long>("oldcountry.playlist_syncs", "{sync}", "Automatic playlist syncs, by outcome.");
        _ytDlpDuration = meter.CreateHistogram<double>(
            "oldcountry.ytdlp.duration", "s", "The duration of each yt-dlp run (search, fetch for playback, download, playlist), with its outcome.",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = [0.5, 1, 2, 3, 5, 8, 13, 20, 30, 60, 120, 300] });
    }

    /// <summary>Records one use case. <paramref name="outcome"/> is handled, refused, failed or cancelled.</summary>
    public void UseCaseEnded(string useCase, string outcome, string? errorCode, TimeSpan elapsed) =>
        _useCaseDuration.Record(elapsed.TotalSeconds,
            new("use_case", useCase), new("outcome", outcome), new("error.code", errorCode ?? ""));

    /// <summary>Records the end of one download job. <paramref name="outcome"/> is completed, failed or postponed.</summary>
    public void DownloadEnded(string outcome, string? errorCode) =>
        _downloads.Add(1, new("outcome", outcome), new("error.code", errorCode ?? ""));

    /// <summary>Records a rate limit. <paramref name="source"/> is downloads or playlist_sync.</summary>
    public void RateLimited(string source) => _rateLimits.Add(1, new KeyValuePair<string, object?>("source", source));

    /// <summary>Records the end of one library scan.</summary>
    public void ScanEnded(string outcome, int tracksAdded)
    {
        _scans.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
        if (tracksAdded > 0) _tracksAdded.Add(tracksAdded);
    }

    /// <summary>Records one yt-dlp run. <paramref name="outcome"/> is ok or failed.</summary>
    public void YtDlpRan(string operation, string outcome, string? errorCode, TimeSpan elapsed) =>
        _ytDlpDuration.Record(elapsed.TotalSeconds,
            new("operation", operation), new("outcome", outcome), new("error.code", errorCode ?? ""));

    /// <summary>Records one automatic sync of a playlist. <paramref name="outcome"/> is synced or failed.</summary>
    public void PlaylistSynced(string outcome) => _playlistSyncs.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
}
