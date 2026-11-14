namespace OldCountry.Application.Realtime;

/// <summary>
/// What changed in the library. The server sends it to each connected app after the commit.
/// It is a notice, not the data: the apps get again what they show.
/// A merge of two notices keeps each flag and playlist, so a burst of changes can go as one message.
/// </summary>
/// <param name="Playlists">Playlists that were made, changed (name, tracks, order, sync) or deleted.</param>
/// <param name="Tracks">Tracks that were added, changed or removed (also a new download).</param>
/// <param name="Downloads">YouTube download jobs that were queued, started, finished or failed.</param>
/// <param name="Everything">The server did not keep some changes, because it was behind. Get everything again.</param>
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
