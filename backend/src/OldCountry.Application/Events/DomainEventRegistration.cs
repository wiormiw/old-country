using Microsoft.Extensions.DependencyInjection.Extensions;
using OldCountry.Domain.Common;

namespace OldCountry.Application.Events;

internal static class DomainEventRegistration
{
    /// <summary>Adds the feed, the handler registry and the dispatcher. A second call changes nothing.</summary>
    public static IServiceCollection AddDomainEvents(this IServiceCollection services)
    {
        // A second registry would split the handlers between two instances,
        // and the dispatcher would see only the handlers of one of them.
        if (Registry(services) is not null) return services;
        services.AddSingleton<DomainEventFeed>();
        services.AddSingleton(new DomainEventHandlers());
        services.AddHostedService<DomainEventDispatcher>();
        return services;
    }

    /// <summary>Runs <typeparamref name="THandler"/> for each committed <typeparamref name="TEvent"/>.</summary>
    internal static IServiceCollection AddDomainEventHandler<TEvent, THandler>(this IServiceCollection services)
        where TEvent : IDomainEvent
        where THandler : class, IDomainEventHandler<TEvent>
    {
        var registry = Registry(services) ?? throw new InvalidOperationException("Call AddDomainEvents() before adding handlers.");
        if (registry.Add<TEvent, THandler>()) services.TryAddScoped<THandler>();
        return services;
    }

    private static DomainEventHandlers? Registry(IServiceCollection services) =>
        services.SingleOrDefault(d => d.ServiceType == typeof(DomainEventHandlers))?.ImplementationInstance as DomainEventHandlers;
}
