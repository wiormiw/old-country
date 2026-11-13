using OldCountry.Domain.Common;

namespace OldCountry.Domain.Library;

public enum DownloadJobState
{
    Queued,
    Running,
    Completed,
    Failed,
}

/// <summary>
/// One requested download of a YouTube video's audio. Stored, so the queue survives restarts.
/// A worker takes a job by starting it with a <em>lease</em>: the job is its own until the lease
/// ends. A worker that dies mid-download (crash, power cut, killed container) can't say so; its
/// lease simply runs out and the job can be started again. After <see cref="MaxAttempts"/> starts
/// that never finished, the job is given up on rather than crashing the worker forever.
/// </summary>
public sealed class DownloadJob : Entity<DownloadJobId>
{
    public const int MaxAttempts = 3;

    private DownloadJob() { }

    public YouTubeVideoId VideoId { get; private init; }

    public DownloadJobState State { get; private set; }

    /// <summary>How many times a worker has started it.</summary>
    public int Attempts { get; private set; }

    /// <summary>While running: until when the worker that started it owns it.</summary>
    public DateTimeOffset? LeaseUntil { get; private set; }

    public DateTimeOffset QueuedAt { get; private init; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? FinishedAt { get; private set; }

    /// <summary>The library track, once completed.</summary>
    public TrackId? TrackId { get; private set; }

    /// <summary>A stable code for why it failed (e.g. <c>youtube.premium_only</c>).</summary>
    public string? ErrorCode { get; private set; }

    public string? Error { get; private set; }

    public bool IsActive => State is DownloadJobState.Queued or DownloadJobState.Running;

    public static DownloadJob Queue(YouTubeVideoId videoId, DateTimeOffset now) =>
        new() { Id = DownloadJobId.New(), VideoId = videoId, State = DownloadJobState.Queued, QueuedAt = now };

    /// <summary>Whether a worker may take it now: queued, or running under a lease that has run out.</summary>
    public bool IsClaimable(DateTimeOffset now) =>
        State == DownloadJobState.Queued || (State == DownloadJobState.Running && LeaseUntil <= now);

    /// <summary>
    /// A worker takes the job until <paramref name="now"/> + <paramref name="lease"/>. <c>false</c>
    /// when it has already been started <see cref="MaxAttempts"/> times without finishing: it is
    /// failed instead, and the worker moves on.
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
    /// Back in the queue without counting the try: YouTube asked us to slow down, which says
    /// nothing about this video. It runs again when the queue resumes.
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
