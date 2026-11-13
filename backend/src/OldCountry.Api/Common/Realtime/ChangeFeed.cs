using System.Threading.Channels;

namespace OldCountry.Api.Common.Realtime;

/// <summary>What the broadcaster sends next: refetch hints and news, merged as they arrive.</summary>
internal sealed record Update(LibraryChanges Changes, IReadOnlyList<Announcement> News)
{
    public bool IsEmpty => Changes.IsEmpty && News.Count == 0;

    public Update Merge(Update other) => new(Changes.Merge(other.Changes), [.. News, .. other.News]);
}

/// <summary>
/// The queue between the database (which reports commits) and the hub (which tells the apps).
/// Writing never blocks or fails a save: when the broadcaster falls far behind, the oldest
/// unsent changes are dropped and replaced by an "everything changed" signal.
/// </summary>
internal sealed class ChangeFeed
{
    /// <summary>Plenty for bursts; the broadcaster merges what it reads, so it rarely holds more than a few.</summary>
    private const int Capacity = 1_000;

    private readonly Channel<Update> _channel;

    // 1 when changes were dropped (set from whichever thread saved); read and reset by the broadcaster.
    private int _overflowed;

    public ChangeFeed() =>
        _channel = Channel.CreateBounded<Update>(
            new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true },
            itemDropped: _ => Interlocked.Exchange(ref _overflowed, 1));

    /// <summary>Whether changes were dropped since the last call; the next message then says everything changed.</summary>
    public bool TakeOverflow() => Interlocked.Exchange(ref _overflowed, 0) == 1;

    public void Publish(LibraryChanges changes) => Write(new Update(changes, []));

    public void Announce(Announcement news) => Write(new Update(LibraryChanges.None, [news]));

    private void Write(Update update)
    {
        if (!update.IsEmpty) _channel.Writer.TryWrite(update);
    }

    public ChannelReader<Update> Reader => _channel.Reader;
}
