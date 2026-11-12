using System.Threading.Channels;
using Microsoft.Extensions.Options;
using OldCountry.Api.Common.Library;
using OldCountry.Api.Common.Messaging;

namespace OldCountry.Api.Features.Tracks.ScanLibrary;

/// <summary>
/// At most one scan waits to run. Asking again while one is waiting changes nothing, so a burst
/// of requests (button mashing, a schedule tick during a manual scan) collapses into one scan.
/// </summary>
internal sealed class LibraryScanQueue
{
    private readonly Channel<ScanTrigger> _channel = Channel.CreateBounded<ScanTrigger>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });

    public ChannelReader<ScanTrigger> Reader => _channel.Reader;

    public bool IsQueued => _channel.Reader.Count > 0;

    /// <summary><c>true</c> when queued; <c>false</c> when a scan was already waiting.</summary>
    public bool Request(ScanTrigger trigger) => _channel.Writer.TryWrite(trigger);
}

/// <summary>Shared between the background scanner (writer) and the status endpoint (reader).</summary>
internal sealed class LibraryScanStatus
{
    private volatile ScanReport? _lastScan;
    private int _running;

    public void Started() => Interlocked.Exchange(ref _running, 1);

    public void Finished(ScanReport? report)
    {
        if (report is not null) _lastScan = report;
        Interlocked.Exchange(ref _running, 0);
    }

    public ScanStatusResponse Snapshot(LibraryScanQueue queue) =>
        new(Volatile.Read(ref _running) == 1, queue.IsQueued, _lastScan);
}

/// <summary>
/// Runs scans one at a time: at startup, on a schedule, and when the API asks. Each scan gets its
/// own DI scope (a fresh DbContext), the same way an HTTP request does.
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
            // Host shutdown.
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
            // A ValidationError (e.g. a root folder vanished) is already logged by the logging decorator.
            if (await handler.Handle(new ScanLibraryCommand(trigger), stoppingToken) is ScanReport finished)
                report = finished;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Since .NET 11 an exception escaping ExecuteAsync stops the whole host. One failed
            // scan must not take the API down; the next trigger simply tries again.
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
