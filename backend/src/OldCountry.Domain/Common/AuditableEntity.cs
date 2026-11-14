namespace OldCountry.Domain.Common;

/// <summary>
/// An entity with the time of its creation and of its last change.
/// The save pipeline writes these values from its clock. The domain only reads them.
/// </summary>
public abstract class AuditableEntity<TId> : Entity<TId> where TId : struct
{
    // The save pipeline writes this value one time, at insert.
    public DateTimeOffset CreatedAt { get; private init; }

    public DateTimeOffset? UpdatedAt { get; private set; }
}
