using System.Threading.Channels;

namespace OldCountry.Application.Tracks.ScanLibrary;

/// <summary>
/// A maximum of one scan waits to run. A new request while one waits changes nothing.
/// Thus a burst of requests (many clicks, a timer tick during a manual scan) becomes one scan.
/// </summary>
public sealed class LibraryScanQueue
{
    private readonly Channel<ScanTrigger> _channel = Channel.CreateBounded<ScanTrigger>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });

    public ChannelReader<ScanTrigger> Reader => _channel.Reader;

    public bool IsQueued => _channel.Reader.Count > 0;

    /// <summary><c>true</c> when the request is queued. <c>false</c> when a scan already waits.</summary>
    public bool Request(ScanTrigger trigger) => _channel.Writer.TryWrite(trigger);
}
