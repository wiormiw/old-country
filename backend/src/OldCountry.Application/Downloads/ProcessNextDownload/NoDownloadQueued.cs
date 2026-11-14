namespace OldCountry.Application.Downloads.ProcessNextDownload;

/// <summary>No job was queued, and no lease had ended.</summary>
public sealed record NoDownloadQueued
{
    public static readonly NoDownloadQueued Value = new();

    private NoDownloadQueued() { }
}
