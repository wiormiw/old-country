using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Application.Abstractions.Persistence;
using OldCountry.Application.Common.Telemetry;
using OldCountry.Application.Common.YouTube;
using OldCountry.Application.Downloads.DownloadTrack;
using OldCountry.Domain.Downloads;
using OldCountry.Domain.Errors;
using OldCountry.Domain.Tracks;

namespace OldCountry.Application.Downloads.ProcessNextDownload;

/// <summary>
/// Claims the oldest download job, runs it, and records the result on the job.
/// Each step uses its own DI scope, so no step keeps a database connection during the download.
/// </summary>
internal sealed partial class ProcessNextDownloadHandler(
    IServiceScopeFactory scopes,
    YouTubeRateLimit rateLimit,
    TimeProvider clock,
    IOptions<YouTubeMusicOptions> options,
    AppMetrics metrics,
    ILogger<ProcessNextDownloadHandler> logger) : ICommandHandler<ProcessNextDownloadCommand, ProcessNextDownloadResult>
{
    /// <summary>Finished jobs are deleted after this time.</summary>
    private static readonly TimeSpan History = TimeSpan.FromDays(30);

    public async Task<ProcessNextDownloadResult> Handle(ProcessNextDownloadCommand command, CancellationToken cancellationToken)
    {
        var job = await ClaimAsync(cancellationToken);
        if (job is null)
        {
            await ForgetOldJobsAsync(cancellationToken);
            return NoDownloadQueued.Value;
        }

        var took = Stopwatch.StartNew();
        await RunAsync(job, cancellationToken);
        return new DownloadProcessed(took.Elapsed);
    }

    /// <summary>
    /// Takes the oldest job that is queued, or that runs under a lease that ended (its worker stopped).
    /// <c>FOR UPDATE SKIP LOCKED</c> makes this safe with more than one worker or server.
    /// Each worker takes a different job, and no worker waits for the lock of another.
    /// </summary>
    private async Task<DownloadJob?> ClaimAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        while (true)
        {
            var now = clock.GetUtcNow();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var candidates = await db.DownloadJobs
                .FromSql($"""
                    SELECT * FROM download_jobs
                    WHERE state = 'Queued' OR (state = 'Running' AND lease_until <= {now})
                    ORDER BY queued_at
                    LIMIT 1
                    FOR UPDATE SKIP LOCKED
                    """)
                .ToListAsync(cancellationToken);
            if (candidates is not [var job]) return null;

            var reclaimed = job.State == DownloadJobState.Running;
            var started = job.Start(now, options.Value.DownloadLease);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            if (reclaimed) Log.Reclaimed(logger, job.VideoId.Value, job.Attempts);
            if (started) return job;
            // The job started too often without an end. It failed in Start. Take the next job.
            Log.GaveUp(logger, job.VideoId.Value);
            metrics.DownloadEnded("failed", "download.gave_up");
        }
    }

    private async Task RunAsync(DownloadJob job, CancellationToken cancellationToken)
    {
        DownloadTrackResult? result = null;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<DownloadTrackCommand, DownloadTrackResult>>();
            result = await handler.Handle(new DownloadTrackCommand(job.VideoId), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The server stops. Put the job back now. Do not wait for its lease to end.
            await FinishAsync(job.Id, j => j.Postpone(), CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            Log.Crashed(logger, exception, job.VideoId.Value);
        }

        var now = clock.GetUtcNow();
        switch (result)
        {
            case TrackId trackId:
                rateLimit.Succeeded();
                await FinishAsync(job.Id, j => j.Complete(trackId, now), cancellationToken);
                metrics.DownloadEnded("completed", null);
                break;
            case ExternalServiceError { Code: YouTubeRefusals.RateLimited } limited:
                var until = rateLimit.RateLimited(now, options.Value.RateLimitPause, limited.Message);
                Log.RateLimited(logger, until, limited.Message);
                await FinishAsync(job.Id, j => j.Postpone(), cancellationToken);
                metrics.RateLimited("downloads");
                metrics.DownloadEnded("postponed", limited.Code);
                break;
            case ExternalServiceError failed:
                await FinishAsync(job.Id, j => j.Fail(failed.Code, failed.Message, now), cancellationToken);
                metrics.DownloadEnded("failed", failed.Code);
                break;
            case ValidationError invalid:
                await FinishAsync(job.Id, j => j.Fail("download.invalid", string.Join(" ", invalid.Errors.Values.SelectMany(m => m)), now), cancellationToken);
                metrics.DownloadEnded("failed", "download.invalid");
                break;
            default:
                // The handler threw an exception.
                await FinishAsync(job.Id, j => j.Fail("download.crashed", "The download failed unexpectedly; see the server log.", now), cancellationToken);
                metrics.DownloadEnded("failed", "download.crashed");
                break;
        }
    }

    private async Task FinishAsync(DownloadJobId id, Action<DownloadJob> change, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        if (await db.DownloadJobs.FindAsync([id], cancellationToken) is not { } job) return;
        change(job);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task ForgetOldJobsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var before = clock.GetUtcNow() - History;
        await db.DownloadJobs
            .Where(j => (j.State == DownloadJobState.Completed || j.State == DownloadJobState.Failed) && j.FinishedAt < before)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 1321, Level = LogLevel.Error, Message = "Download of {VideoId} crashed")]
        public static partial void Crashed(ILogger logger, Exception exception, string videoId);

        [LoggerMessage(EventId = 1322, Level = LogLevel.Warning,
            Message = "YouTube is rate limiting downloads; the queue rests until {Until}: {Reason}")]
        public static partial void RateLimited(ILogger logger, DateTimeOffset until, string reason);

        [LoggerMessage(EventId = 1323, Level = LogLevel.Warning,
            Message = "Took over the download of {VideoId}: its lease ran out without it finishing (attempt {Attempts})")]
        public static partial void Reclaimed(ILogger logger, string videoId, int attempts);

        [LoggerMessage(EventId = 1324, Level = LogLevel.Error, Message = "Gave up on the download of {VideoId}: it never finished in 3 tries")]
        public static partial void GaveUp(ILogger logger, string videoId);
    }
}
