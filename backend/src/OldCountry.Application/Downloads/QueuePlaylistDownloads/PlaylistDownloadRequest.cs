namespace OldCountry.Application.Downloads.QueuePlaylistDownloads;

/// <summary>Downloads each track of a playlist that is not downloaded yet.</summary>
public sealed record PlaylistDownloadRequest
{
    /// <summary>
    /// Also try again the tracks that YouTube refused for a lasting reason (Premium only, private, removed,
    /// age or region restricted), for example after you get Premium or add cookies. The default is off.
    /// </summary>
    public bool RetrySkipped { get; init; }
}
