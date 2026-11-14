using FluentValidation;
using Microsoft.EntityFrameworkCore;
using OldCountry.Application.Abstractions.Persistence;
using OldCountry.Domain.Downloads;
using OldCountry.Domain.YouTube;

namespace OldCountry.Application.Downloads;

internal static class DownloadStatuses
{
    public static DownloadStatus ToStatus(this DownloadJob job, string? title) => new(
        job.Id.Value,
        job.VideoId.Value,
        title,
        job.State switch
        {
            DownloadJobState.Queued => DownloadState.Queued,
            DownloadJobState.Running => DownloadState.Running,
            DownloadJobState.Completed => DownloadState.Completed,
            _ => DownloadState.Failed,
        },
        job.TrackId?.Value,
        job.ErrorCode,
        job.Error,
        job.Attempts,
        job.QueuedAt,
        job.FinishedAt);

    /// <summary>The titles of the library tracks of these videos.</summary>
    public static async Task<Dictionary<YouTubeVideoId, string>> TitlesAsync(
        this IAppDbContext db, IEnumerable<YouTubeVideoId> videos, CancellationToken cancellationToken)
    {
        var ids = videos.Select(v => (YouTubeVideoId?)v).Distinct().ToList();
        return await db.Tracks
            .Where(t => ids.Contains(t.YouTubeVideoId))
            .Select(t => new { Video = t.YouTubeVideoId!.Value, t.Title })
            .ToDictionaryAsync(t => t.Video, t => t.Title, cancellationToken);
    }

    /// <summary>
    /// Makes one job for each video. When a job for a video is already queued or running, it uses that job.
    /// It saves the jobs, then it wakes the worker.
    /// </summary>
    public static async Task<List<DownloadJob>> QueueAsync(
        this IAppDbContext db, IReadOnlyCollection<YouTubeVideoId> videos, DownloadDoorbell doorbell, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var active = await db.DownloadJobs
            .Where(j => videos.Contains(j.VideoId) && (j.State == DownloadJobState.Queued || j.State == DownloadJobState.Running))
            .ToDictionaryAsync(j => j.VideoId, cancellationToken);
        var jobs = new List<DownloadJob>(videos.Count);
        foreach (var video in videos.Distinct())
        {
            if (!active.TryGetValue(video, out var job))
            {
                job = DownloadJob.Queue(video, now);
                db.DownloadJobs.Add(job);
            }
            jobs.Add(job);
        }
        await db.SaveChangesAsync(cancellationToken);
        doorbell.Ring();   // After the commit (the callers have no outer transaction), so the worker finds the jobs.
        return jobs;
    }
}
