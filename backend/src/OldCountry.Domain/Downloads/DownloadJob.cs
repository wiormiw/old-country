using OldCountry.Domain.Common;
using OldCountry.Domain.Tracks;
using OldCountry.Domain.YouTube;

namespace OldCountry.Domain.Downloads;

/// <summary>
/// One request to download the audio of a YouTube video. The database keeps it, so the queue survives a restart.
/// A worker takes a job with a <em>lease</em>: the job belongs to that worker until the lease ends.
/// A worker that stops during a download (crash, power cut) cannot report it. Its lease ends, and another start is possible.
/// After <see cref="MaxAttempts"/> starts without an end, the job fails. Thus one bad job cannot stop the worker.
/// </summary>
public sealed class DownloadJob : Entity<DownloadJobId>
{
    public const int MaxAttempts = 3;

    private DownloadJob() { }

    public YouTubeVideoId VideoId { get; private init; }

    public DownloadJobState State { get; private set; }

    /// <summary>The number of times that a worker started the job.</summary>
    public int Attempts { get; private set; }

    /// <summary>While the job runs: the time until which the worker owns it.</summary>
    public DateTimeOffset? LeaseUntil { get; private set; }

    public DateTimeOffset QueuedAt { get; private init; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? FinishedAt { get; private set; }

    /// <summary>The library track, after the job completes.</summary>
    public TrackId? TrackId { get; private set; }

    /// <summary>The stable code of the failure, for example <c>youtube.premium_only</c>.</summary>
    public string? ErrorCode { get; private set; }

    public string? Error { get; private set; }

    public bool IsActive => State is DownloadJobState.Queued or DownloadJobState.Running;

    public static DownloadJob Queue(YouTubeVideoId videoId, DateTimeOffset now) =>
        new() { Id = DownloadJobId.New(), VideoId = videoId, State = DownloadJobState.Queued, QueuedAt = now };

    /// <summary>True when a worker can take the job now: it is queued, or its lease has ended.</summary>
    public bool IsClaimable(DateTimeOffset now) =>
        State == DownloadJobState.Queued || (State == DownloadJobState.Running && LeaseUntil <= now);

    /// <summary>
    /// A worker takes the job until <paramref name="now"/> + <paramref name="lease"/>.
    /// Returns <c>false</c> when the job already started <see cref="MaxAttempts"/> times without an end.
    /// The job then fails, and the worker continues with the next job.
    /// </summary>
    public bool Start(DateTimeOffset now, TimeSpan lease)
    {
        if (!IsClaimable(now)) return false;
        if (Attempts >= MaxAttempts)
        {
            Finish(DownloadJobState.Failed, now, null, "download.gave_up",
                $"Stopped after {MaxAttempts} tries that never finished; see the server log.");
            return false;
        }
        Attempts++;
        State = DownloadJobState.Running;
        StartedAt = now;
        LeaseUntil = now + lease;
        return true;
    }

    public void Complete(TrackId trackId, DateTimeOffset now) => Finish(DownloadJobState.Completed, now, trackId, null, null);

    public void Fail(string code, string message, DateTimeOffset now) => Finish(DownloadJobState.Failed, now, null, code, message);

    /// <summary>
    /// Puts the job back in the queue, and does not count the attempt.
    /// YouTube asked the server to slow down. This is not a problem of this video.
    /// The job runs again when the queue continues.
    /// </summary>
    public void Postpone()
    {
        if (State != DownloadJobState.Running) return;
        State = DownloadJobState.Queued;
        Attempts = Math.Max(0, Attempts - 1);
        LeaseUntil = null;
    }

    private void Finish(DownloadJobState state, DateTimeOffset now, TrackId? trackId, string? code, string? message)
    {
        State = state;
        FinishedAt = now;
        LeaseUntil = null;
        TrackId = trackId;
        ErrorCode = code;
        Error = message;
        Raise(state == DownloadJobState.Completed
            ? new DownloadFinished(Id, VideoId, trackId!.Value)
            : new DownloadFailed(Id, VideoId, code!, message!));
    }
}
