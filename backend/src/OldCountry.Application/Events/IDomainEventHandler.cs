using OldCountry.Domain.Common;

namespace OldCountry.Application.Events;

/// <summary>Reacts to one type of domain event, after the change that raised it commits.</summary>
internal interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task Handle(TEvent domainEvent, CancellationToken cancellationToken);
}
