using System.Threading.Channels;

namespace OldCountry.Application.Realtime;

/// <summary>
/// The queue between the database (which reports commits) and the hub (which tells the apps).
/// A write never blocks a save or makes it fail. When the broadcaster is far behind, the feed drops the oldest
/// changes and sends an "everything changed" signal in their place.
/// </summary>
public sealed class ChangeFeed
{
    /// <summary>Enough for bursts. The broadcaster merges what it reads, so it seldom holds more than a few.</summary>
    private const int Capacity = 1_000;

    private readonly Channel<Update> _channel;

    // 1 when the feed dropped changes (any thread that saves can set it). The broadcaster reads and resets it.
    private int _overflowed;

    public ChangeFeed() =>
        _channel = Channel.CreateBounded<Update>(
            new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true },
            itemDropped: _ => Interlocked.Exchange(ref _overflowed, 1));

    /// <summary>True when the feed dropped changes since the last call. The next message then says that everything changed.</summary>
    public bool TakeOverflow() => Interlocked.Exchange(ref _overflowed, 0) == 1;

    public void Publish(LibraryChanges changes) => Write(new Update(changes, []));

    public void Announce(Announcement news) => Write(new Update(LibraryChanges.None, [news]));

    private void Write(Update update)
    {
        if (!update.IsEmpty) _channel.Writer.TryWrite(update);
    }

    public ChannelReader<Update> Reader => _channel.Reader;
}
