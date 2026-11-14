namespace OldCountry.Domain.Artists;

/// <summary>
/// The id of an artist. It is a separate type, so the compiler stops the use of a different id in its place.
/// Version 7 GUIDs sort by creation time, so new index entries go to the end of the B-tree.
/// </summary>
public readonly record struct ArtistId(Guid Value)
{
    public static ArtistId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
