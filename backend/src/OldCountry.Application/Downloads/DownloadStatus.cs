namespace OldCountry.Application.Downloads;

/// <summary>The state of one download.</summary>
/// <param name="JobId">The id of this download.</param>
/// <param name="VideoId">The YouTube video of the download.</param>
/// <param name="Title">The title of the track, when it is a library track.</param>
/// <param name="State">Queued, Running, Completed or Failed.</param>
/// <param name="TrackId">The library track, after the download completes.</param>
/// <param name="ErrorCode">The reason for the failure, as a stable code (<c>youtube.premium_only</c>, <c>youtube.private</c>, ...).</param>
/// <param name="Error">The reason for the failure, in words.</param>
/// <param name="Attempts">The number of times that the download started.</param>
/// <param name="QueuedAt">When the user asked for it.</param>
/// <param name="FinishedAt">When it completed or failed.</param>
public sealed record DownloadStatus(
    Guid JobId,
    string VideoId,
    string? Title,
    DownloadState State,
    Guid? TrackId,
    string? ErrorCode,
    string? Error,
    int Attempts,
    DateTimeOffset QueuedAt,
    DateTimeOffset? FinishedAt);
