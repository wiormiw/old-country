namespace OldCountry.Application.Tracks.ScanLibrary;

/// <summary>The scan state. The scan loop writes it, and the status endpoint reads it.</summary>
public sealed class LibraryScanStatus
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
