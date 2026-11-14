namespace OldCountry.Domain.Common;

/// <summary>An entity that keeps its domain events until persistence takes them.</summary>
public interface IHasDomainEvents
{
    IReadOnlyList<IDomainEvent> DomainEvents { get; }

    void ClearDomainEvents();
}
