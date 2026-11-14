using System.Threading.Channels;
using OldCountry.Domain.Common;

namespace OldCountry.Application.Events;

/// <summary>
/// Committed domain events on their way to their handlers. Persistence writes here after a commit.
/// <see cref="DomainEventDispatcher"/> reads. A write never blocks a save.
///
/// The feed is in memory only. The events cause side effects (news for the apps), so the loss of
/// a few events when the server stops is acceptable. An event that must never be lost needs an
/// outbox table, written in the same transaction.
/// </summary>
public sealed class DomainEventFeed
{
    private const int Capacity = 10_000;

    private readonly Channel<IDomainEvent> _channel = Channel.CreateBounded<IDomainEvent>(
        new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    public void Publish(IEnumerable<IDomainEvent> events)
    {
        foreach (var domainEvent in events) _channel.Writer.TryWrite(domainEvent);
    }

    public ChannelReader<IDomainEvent> Reader => _channel.Reader;
}
