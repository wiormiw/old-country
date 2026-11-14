namespace OldCountry.Domain.Common;

/// <summary>An object with an identity that stays the same when its values change.</summary>
public abstract class Entity<TId> : IHasDomainEvents where TId : struct
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public TId Id { get; protected init; }

    /// <summary>The events that this entity raised since the last save. The database does not keep them.</summary>
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    public void ClearDomainEvents() => _domainEvents.Clear();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
}
