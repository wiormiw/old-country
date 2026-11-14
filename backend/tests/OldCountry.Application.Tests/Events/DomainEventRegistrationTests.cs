using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OldCountry.Application.Events;
using OldCountry.Domain.Common;

namespace OldCountry.Application.Tests.Events;

public sealed class DomainEventRegistrationTests
{
    private sealed record Happened(int N) : IDomainEvent;

    private sealed record AlsoHappened : IDomainEvent;

    /// <summary>Counts the events that it handles, of both types (one class, two event types).</summary>
    private sealed class Counter : IDomainEventHandler<Happened>, IDomainEventHandler<AlsoHappened>
    {
        public static int Seen;

        public Task Handle(Happened domainEvent, CancellationToken cancellationToken) => Count();

        public Task Handle(AlsoHappened domainEvent, CancellationToken cancellationToken) => Count();

        private static Task Count()
        {
            Interlocked.Increment(ref Seen);
            return Task.CompletedTask;
        }
    }

    private sealed class Other : IDomainEventHandler<Happened>
    {
        public Task Handle(Happened domainEvent, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public void Adding_domain_events_twice_keeps_one_registry_and_one_dispatcher()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddDomainEvents();
        services.AddDomainEventHandler<Happened, Counter>();
        services.AddDomainEvents();

        using var provider = services.BuildServiceProvider();
        provider.GetServices<DomainEventHandlers>().ShouldHaveSingleItem().For(typeof(Happened)).Count.ShouldBe(1);
        provider.GetServices<IHostedService>().OfType<DomainEventDispatcher>().ShouldHaveSingleItem();
    }

    [Fact]
    public void A_handler_registered_twice_runs_once_but_different_handlers_all_run()
    {
        var services = new ServiceCollection().AddLogging().AddDomainEvents();
        services.AddDomainEventHandler<Happened, Counter>();
        services.AddDomainEventHandler<Happened, Counter>();
        services.AddDomainEventHandler<Happened, Other>();
        services.AddDomainEventHandler<AlsoHappened, Counter>();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<DomainEventHandlers>();
        registry.For(typeof(Happened)).Count.ShouldBe(2);
        registry.For(typeof(AlsoHappened)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task One_class_handling_two_event_types_is_resolved_as_itself()
    {
        var services = new ServiceCollection().AddLogging().AddDomainEvents();
        services.AddDomainEventHandler<Happened, Counter>();
        services.AddDomainEventHandler<AlsoHappened, Counter>();
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<DomainEventHandlers>();
        Counter.Seen = 0;

        await using var scope = provider.CreateAsyncScope();
        foreach (var handle in registry.For(typeof(Happened))) await handle(scope.ServiceProvider, new Happened(1), TestContext.Current.CancellationToken);
        foreach (var handle in registry.For(typeof(AlsoHappened))) await handle(scope.ServiceProvider, new AlsoHappened(), TestContext.Current.CancellationToken);

        Counter.Seen.ShouldBe(2);
    }
}
