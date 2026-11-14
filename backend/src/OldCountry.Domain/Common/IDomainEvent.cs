namespace OldCountry.Domain.Common;

/// <summary>
/// A fact about a change in the domain, for example "this download finished".
/// An entity raises it during the change. Persistence sends it out only after the change commits.
/// Thus no handler reacts to a change that did not occur.
/// </summary>
public interface IDomainEvent;
