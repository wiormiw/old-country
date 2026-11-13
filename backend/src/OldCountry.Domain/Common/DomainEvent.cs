namespace OldCountry.Domain.Common;

/// <summary>
/// Something that happened in the domain, worth telling others about: "this download finished",
/// "these songs arrived from YouTube". Raised by entities as part of a change, and handed out by
/// persistence only once that change has committed, so nobody hears about what didn't happen.
/// </summary>
public interface IDomainEvent;

/// <summary>Entities that record domain events until persistence takes them.</summary>
public interface IHasDomainEvents
{
    IReadOnlyList<IDomainEvent> DomainEvents { get; }

    void ClearDomainEvents();
}
