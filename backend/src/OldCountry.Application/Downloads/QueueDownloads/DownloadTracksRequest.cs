namespace OldCountry.Application.Downloads.QueueDownloads;

/// <summary>Videos to download into the library, so that they can play without a connection.</summary>
public sealed record DownloadTracksRequest
{
    /// <summary>YouTube video ids, 1-50 for each request.</summary>
    public required IReadOnlyList<string> VideoIds { get; init; }
}
