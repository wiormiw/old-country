namespace OldCountry.Application.Downloads.QueuePlaylistDownloads;

/// <summary>The result of a request for the downloads of a playlist.</summary>
/// <param name="Queued">Tracks queued now.</param>
/// <param name="AlreadyDownloaded">Tracks that can already play without a connection.</param>
/// <param name="AlreadyQueued">Tracks that already wait or download.</param>
/// <param name="Skipped">Tracks that YouTube refused before for a lasting reason. Use <c>retrySkipped</c> to try them again.</param>
/// <param name="NotOnYouTube">Tracks without a YouTube video (local files only).</param>
public sealed record PlaylistDownloadReport(int Queued, int AlreadyDownloaded, int AlreadyQueued, int Skipped, int NotOnYouTube);
