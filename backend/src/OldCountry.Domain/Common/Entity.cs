namespace OldCountry.Domain.Common;

/// <summary>Something with an identity that outlives its attribute values.</summary>
public abstract class Entity<TId> : IHasDomainEvents where TId : struct
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public TId Id { get; protected init; }

    /// <summary>What this entity has raised since persistence last took its events. Not stored.</summary>
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    public void ClearDomainEvents() => _domainEvents.Clear();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
}

/// <summary>
/// Entities whose creation and last change are stamped by persistence. The domain only reads
/// these; the values come from the save pipeline's clock.
/// </summary>
public abstract class AuditableEntity<TId> : Entity<TId> where TId : struct
{
    // Written once by the save pipeline on insert, never again.
    public DateTimeOffset CreatedAt { get; private init; }

    public DateTimeOffset? UpdatedAt { get; private set; }
}
