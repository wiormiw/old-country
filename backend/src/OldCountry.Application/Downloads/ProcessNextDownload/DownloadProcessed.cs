namespace OldCountry.Application.Downloads.ProcessNextDownload;

/// <summary>A job ended (completed, failed or postponed). <paramref name="Took"/> tells the worker if YouTube was contacted.</summary>
public sealed record DownloadProcessed(TimeSpan Took);
