using Microsoft.Extensions.Options;
using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Application.Common.Library;
using OldCountry.Application.Tracks.ScanLibrary;

namespace OldCountry.Infrastructure.Background;

/// <summary>
/// Runs the scans one at a time: at startup, on a schedule, and when the API asks.
/// Each scan gets its own DI scope (a new DbContext), the same as an HTTP request.
/// </summary>
internal sealed partial class LibraryScanner(
    IServiceScopeFactory scopes,
    LibraryScanQueue queue,
    LibraryScanStatus status,
    IOptions<LibraryOptions> options,
    TimeProvider clock,
    ILogger<LibraryScanner> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var library = options.Value;
        var schedule = Task.CompletedTask;
        if (library.Roots.Count == 0)
        {
            Log.NotConfigured(logger);
        }
        else
        {
            if (library.ScanOnStartup) queue.Request(ScanTrigger.Startup);
            if (library.RescanInterval > TimeSpan.Zero) schedule = ScheduleAsync(library.RescanInterval, stoppingToken);
        }

        try
        {
            await foreach (var trigger in queue.Reader.ReadAllAsync(stoppingToken))
                await RunAsync(trigger, stoppingToken);
            await schedule;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The server stops.
        }
    }

    private async Task ScheduleAsync(TimeSpan interval, CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(interval, clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            queue.Request(ScanTrigger.Scheduled);
    }

    private async Task RunAsync(ScanTrigger trigger, CancellationToken stoppingToken)
    {
        status.Started();
        ScanReport? report = null;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<ScanLibraryCommand, ScanLibraryResult>>();
            // The logging decorator already logged a ValidationError (for example, a root folder that is gone).
            if (await handler.Handle(new ScanLibraryCommand(trigger), stoppingToken) is ScanReport finished)
                report = finished;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Since .NET 11, an exception that leaves ExecuteAsync stops the full host.
            // One failed scan must not stop the API. The next trigger tries again.
            Log.ScanCrashed(logger, exception, trigger);
        }
        finally
        {
            status.Finished(report);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 1110, Level = LogLevel.Information, Message = "Library scanning is off: no folders configured in Library:Roots")]
        public static partial void NotConfigured(ILogger logger);

        [LoggerMessage(EventId = 1111, Level = LogLevel.Error, Message = "Library scan ({Trigger}) crashed; will retry on the next trigger")]
        public static partial void ScanCrashed(ILogger logger, Exception exception, ScanTrigger trigger);
    }
}
