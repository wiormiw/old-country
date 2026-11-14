using OldCountry.Domain.Common;

namespace OldCountry.Application.Events;

/// <summary>
/// The handlers of each event type. <see cref="DomainEventRegistration.AddDomainEventHandler{TEvent,THandler}"/> fills it,
/// with one line for each handler. There is no assembly scan, the same as for commands and queries.
/// </summary>
internal sealed class DomainEventHandlers
{
    private readonly Dictionary<Type, List<Func<IServiceProvider, IDomainEvent, CancellationToken, Task>>> _byType = [];
    private readonly HashSet<(Type Event, Type Handler)> _registered = [];

    /// <summary>
    /// Adds a handler for an event. An event can have many different handlers.
    /// When the same pair is added two times, it is kept one time. Thus a repeated registration cannot make it run two times.
    /// </summary>
    /// <returns>True when the pair is new.</returns>
    public bool Add<TEvent, THandler>()
        where TEvent : IDomainEvent
        where THandler : IDomainEventHandler<TEvent>
    {
        if (!_registered.Add((typeof(TEvent), typeof(THandler)))) return false;
        if (!_byType.TryGetValue(typeof(TEvent), out var handlers)) _byType[typeof(TEvent)] = handlers = [];
        // Get the concrete handler from the scope of the event, because one class can handle many event types.
        handlers.Add((services, domainEvent, cancellationToken) =>
            services.GetRequiredService<THandler>().Handle((TEvent)domainEvent, cancellationToken));
        return true;
    }

    public IReadOnlyList<Func<IServiceProvider, IDomainEvent, CancellationToken, Task>> For(Type eventType) =>
        _byType.TryGetValue(eventType, out var handlers) ? handlers : [];
}
