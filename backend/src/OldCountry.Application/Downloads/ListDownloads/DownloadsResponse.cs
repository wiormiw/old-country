namespace OldCountry.Application.Downloads.ListDownloads;

/// <summary>The recent downloads, and the rest of the queue if there is one.</summary>
/// <param name="Jobs">The most recent downloads, newest first.</param>
/// <param name="PausedUntil">When YouTube asked the server to slow down: the time when the downloads continue.</param>
/// <param name="PauseReason">The text from YouTube.</param>
public sealed record DownloadsResponse(IReadOnlyList<DownloadStatus> Jobs, DateTimeOffset? PausedUntil, string? PauseReason);
