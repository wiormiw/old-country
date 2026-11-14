using Microsoft.Extensions.Options;
using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Application.Common.YouTube;
using OldCountry.Application.Downloads;
using OldCountry.Application.Downloads.ProcessNextDownload;

namespace OldCountry.Infrastructure.Background;

/// <summary>
/// Runs the download queue: one job at a time, with a pause between jobs.
/// The work of one job is <see cref="ProcessNextDownloadCommand"/>. This loop only waits and repeats.
/// </summary>
/// <remarks>
/// The flow of a download:
/// <list type="number">
/// <item>An endpoint adds rows to <c>download_jobs</c> (state Queued) and rings the doorbell.</item>
/// <item>This loop wakes on the doorbell, or each 30 seconds, and sends the command.</item>
/// <item>The command claims the oldest job, downloads it with yt-dlp, and marks the job.</item>
/// <item>When YouTube limits the rate, the queue rests. Then the loop waits <c>YouTubeMusic:DownloadPause</c>.</item>
/// </list>
/// </remarks>
internal sealed partial class DownloadWorker(
    IServiceScopeFactory scopes,
    DownloadDoorbell doorbell,
    YouTubeRateLimit rateLimit,
    TimeProvider clock,
    IOptions<YouTubeMusicOptions> options,
    ILogger<DownloadWorker> logger) : BackgroundService
{
    /// <summary>Look for jobs at this interval also without a ring: for leases that ended, or jobs from another server.</summary>
    private static readonly TimeSpan IdleCheck = TimeSpan.FromSeconds(30);

    /// <summary>A download shorter than this did not contact YouTube (the file was already there). No pause is necessary after it.</summary>
    private static readonly TimeSpan Instant = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (rateLimit.Remaining(clock.GetUtcNow()) is { } rest)
                {
                    await Task.Delay(rest, clock, stoppingToken);
                    continue;
                }

                ProcessNextDownloadResult result;
                await using (var scope = scopes.CreateAsyncScope())
                {
                    var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<ProcessNextDownloadCommand, ProcessNextDownloadResult>>();
                    result = await handler.Handle(new ProcessNextDownloadCommand(), stoppingToken);
                }

                switch (result)
                {
                    case NoDownloadQueued:
                        await doorbell.WaitAsync(IdleCheck, stoppingToken);
                        break;
                    case DownloadProcessed { Took: var took } when took > Instant && options.Value.DownloadPause > TimeSpan.Zero:
                        await Task.Delay(options.Value.DownloadPause, clock, stoppingToken);
                        break;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // For example, the database is not available. Since .NET 11, an exception that
                // leaves a BackgroundService stops the host. Thus log it and try again soon.
                Log.LoopFailed(logger, exception);
                await Task.Delay(TimeSpan.FromSeconds(10), clock, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 1325, Level = LogLevel.Error, Message = "The download worker hit an error; trying again in 10 s")]
        public static partial void LoopFailed(ILogger logger, Exception exception);
    }
}
