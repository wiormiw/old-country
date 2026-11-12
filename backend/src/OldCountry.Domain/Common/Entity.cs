namespace OldCountry.Domain.Common;

/// <summary>Something with an identity that outlives its attribute values.</summary>
public abstract class Entity<TId> where TId : struct
{
    public TId Id { get; protected init; }
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
