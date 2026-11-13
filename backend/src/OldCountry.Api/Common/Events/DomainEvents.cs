using System.Threading.Channels;
using OldCountry.Domain.Common;

namespace OldCountry.Api.Common.Events;

/// <summary>Reacts to one kind of domain event, after the change that raised it has committed.</summary>
internal interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task Handle(TEvent domainEvent, CancellationToken cancellationToken);
}

/// <summary>
/// Committed domain events on their way to their handlers. Persistence writes here after a
/// commit; <see cref="DomainEventDispatcher"/> reads. Writing never blocks a save.
///
/// In memory only: events are side effects (telling apps, later maybe notifications), so losing
/// the few in flight when the server stops is acceptable. Something that must never be lost
/// would need an outbox table written in the same transaction instead.
/// </summary>
internal sealed class DomainEventFeed
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

/// <summary>
/// Which handlers each event type has. Filled by <see cref="DomainEventRegistration.AddDomainEventHandler{TEvent,THandler}"/>,
/// one explicit line per handler (no assembly scanning, as with commands and queries).
/// </summary>
internal sealed class DomainEventHandlers
{
    private readonly Dictionary<Type, List<Func<IServiceProvider, IDomainEvent, CancellationToken, Task>>> _byType = [];

    public void Add<TEvent, THandler>()
        where TEvent : IDomainEvent
        where THandler : IDomainEventHandler<TEvent>
    {
        if (!_byType.TryGetValue(typeof(TEvent), out var handlers)) _byType[typeof(TEvent)] = handlers = [];
        handlers.Add((services, domainEvent, cancellationToken) =>
            services.GetRequiredService<THandler>().Handle((TEvent)domainEvent, cancellationToken));
    }

    public IReadOnlyList<Func<IServiceProvider, IDomainEvent, CancellationToken, Task>> For(Type eventType) =>
        _byType.TryGetValue(eventType, out var handlers) ? handlers : [];
}

/// <summary>
/// Runs each committed event's handlers, one event at a time, each in its own DI scope (so a
/// handler gets a fresh DbContext). A failing handler is logged and doesn't stop the others.
/// </summary>
internal sealed partial class DomainEventDispatcher(
    DomainEventFeed feed,
    DomainEventHandlers handlers,
    IServiceScopeFactory scopes,
    ILogger<DomainEventDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var domainEvent in feed.Reader.ReadAllAsync(stoppingToken))
            {
                foreach (var handle in handlers.For(domainEvent.GetType()))
                {
                    try
                    {
                        await using var scope = scopes.CreateAsyncScope();
                        await handle(scope.ServiceProvider, domainEvent, stoppingToken);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                    {
                        LogHandlerFailed(exception, domainEvent.GetType().Name);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "A {Event} handler failed")]
    private partial void LogHandlerFailed(Exception exception, string @event);
}

internal static class DomainEventRegistration
{
    public static IServiceCollection AddDomainEvents(this IServiceCollection services)
    {
        services.AddSingleton<DomainEventFeed>();
        services.AddSingleton(new DomainEventHandlers());
        services.AddHostedService<DomainEventDispatcher>();
        return services;
    }

    /// <summary>Runs <typeparamref name="THandler"/> for every committed <typeparamref name="TEvent"/>.</summary>
    public static IServiceCollection AddDomainEventHandler<TEvent, THandler>(this IServiceCollection services)
        where TEvent : IDomainEvent
        where THandler : class, IDomainEventHandler<TEvent>
    {
        var registry = services.LastOrDefault(d => d.ServiceType == typeof(DomainEventHandlers))?.ImplementationInstance as DomainEventHandlers
            ?? throw new InvalidOperationException("Call AddDomainEvents() before adding handlers.");
        registry.Add<TEvent, THandler>();
        services.AddScoped<THandler>();
        return services;
    }
}
