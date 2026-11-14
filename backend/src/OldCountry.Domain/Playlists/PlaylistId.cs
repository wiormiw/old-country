namespace OldCountry.Domain.Playlists;

/// <summary>
/// The id of a playlist. It is a separate type, so the compiler stops the use of a different id in its place.
/// Version 7 GUIDs sort by creation time, so new index entries go to the end of the B-tree.
/// </summary>
public readonly record struct PlaylistId(Guid Value)
{
    public static PlaylistId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
