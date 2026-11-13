namespace OldCountry.Api.Common.Realtime;

/// <summary>
/// What changed in the library, sent to every connected app after the change has committed.
/// It's a hint, not the data: apps refetch what they're showing. Merging two of these keeps
/// every flag and playlist, so a burst of changes can travel as one message.
/// </summary>
/// <param name="Playlists">Playlists created, changed (name, songs, order, sync) or deleted.</param>
/// <param name="Tracks">Songs added, changed or removed (including becoming downloaded).</param>
/// <param name="Downloads">YouTube download jobs queued, progressing, finished or failed.</param>
/// <param name="Everything">Some changes weren't tracked (the server fell behind): refetch everything.</param>
public sealed record LibraryChanges(IReadOnlyList<Guid> Playlists, bool Tracks, bool Downloads, bool Everything = false)
{
    public static readonly LibraryChanges None = new([], false, false);

    public bool IsEmpty => Playlists.Count == 0 && !Tracks && !Downloads && !Everything;

    public LibraryChanges Merge(LibraryChanges other) => new(
        Playlists.Union(other.Playlists).ToArray(),
        Tracks || other.Tracks,
        Downloads || other.Downloads,
        Everything || other.Everything);
}
