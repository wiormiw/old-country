using OldCountry.Domain.Common;
using OldCountry.Domain.Errors;
using OldCountry.Domain.Tracks;
using OldCountry.Domain.YouTube;

namespace OldCountry.Domain.Playlists;

/// <summary>
/// An ordered list of tracks. The playlist owns its entries as a value collection.
/// An entry has no identity, only a position. Only the methods of this class change the entries.
/// </summary>
public sealed class Playlist : AuditableEntity<PlaylistId>
{
    public const int NameMaxLength = 200;
    public const int DescriptionMaxLength = 2000;
    public const int MaxEntries = 10_000;
    public const int YouTubeListIdMaxLength = 80;

    private readonly List<PlaylistEntry> _entries = [];

    private Playlist() { }

    public string Name { get; private set; } = null!;

    public string? Description { get; private set; }

    public IReadOnlyList<PlaylistEntry> Entries => _entries;

    /// <summary>
    /// The list id of the source YouTube Music playlist (for example <c>PL…</c>, or <c>LM</c> for liked songs).
    /// It is <c>null</c> for a playlist made here. A new import of that playlist updates this playlist.
    /// </summary>
    public string? YouTubeListId { get; private set; }

    /// <summary>The last time that an import or a sync took the tracks of the YouTube playlist.</summary>
    public DateTimeOffset? YouTubeSyncedAt { get; private set; }

    /// <summary>
    /// Each video that this playlist took from its YouTube playlist.
    /// A sync adds only videos that are not in this list.
    /// Thus a track that the user removed here stays removed, also when it is still on YouTube.
    /// </summary>
    private readonly List<string> _youTubeVideosTaken = [];

    public static Validated<Playlist> Create(string name, string? description) =>
        Check(name, description) switch
        {
            { } error => error,
            null => new Playlist { Id = PlaylistId.New(), Name = name.Trim(), Description = Normalize(description) },
        };

    /// <summary>Changes the name and the description. Returns the reason when the values are not valid.</summary>
    public ValidationError? Rename(string name, string? description)
    {
        if (Check(name, description) is { } error) return error;
        Name = name.Trim();
        Description = Normalize(description);
        return null;
    }

    /// <summary>Adds tracks at the end, in the given order. Refuses all of them when the playlist becomes too long.</summary>
    public ValidationError? Append(IReadOnlyCollection<TrackId> trackIds, DateTimeOffset addedAt)
    {
        if (_entries.Count + trackIds.Count > MaxEntries)
            return ValidationError.For("trackIds", $"A playlist holds at most {MaxEntries} tracks.");
        _entries.AddRange(trackIds.Select(id => new PlaylistEntry(id, addedAt)));
        return null;
    }

    /// <summary>Returns <c>false</c> when no entry has that position.</summary>
    public bool RemoveAt(int position)
    {
        if (!HasPosition(position)) return false;
        _entries.RemoveAt(position);
        return true;
    }

    /// <summary>Moves one entry. The other entries shift. Returns <c>false</c> when a position is out of range.</summary>
    public bool Move(int from, int to)
    {
        if (!HasPosition(from) || !HasPosition(to)) return false;
        var entry = _entries[from];
        _entries.RemoveAt(from);
        _entries.Insert(to, entry);
        return true;
    }

    /// <summary>Keeps the source YouTube playlist, so that a new import of it does a sync.</summary>
    public ValidationError? LinkToYouTube(string listId)
    {
        if (string.IsNullOrWhiteSpace(listId) || listId.Length > YouTubeListIdMaxLength)
            return ValidationError.For("youTubeListId", $"Must be 1-{YouTubeListIdMaxLength} characters.");
        YouTubeListId = listId;
        return null;
    }

    /// <summary>
    /// The videos of a YouTube playlist that a sync adds, in the order of YouTube, each one time.
    /// These are the videos that this playlist did not take before and does not hold now (<paramref name="present"/>).
    /// </summary>
    public IReadOnlyList<YouTubeVideoId> NewOnYouTube(IEnumerable<YouTubeVideoId> source, IReadOnlySet<YouTubeVideoId> present)
    {
        var taken = _youTubeVideosTaken.ToHashSet(StringComparer.Ordinal);
        return [.. source.Distinct().Where(video => !taken.Contains(video.Value) && !present.Contains(video))];
    }

    /// <summary>Records that the playlist was compared with its YouTube playlist now.</summary>
    public void MarkSyncedFromYouTube(DateTimeOffset at) => YouTubeSyncedAt = at;

    /// <summary>Records videos as taken from YouTube, but does not add them, because they are already here.</summary>
    public void MarkTakenFromYouTube(IEnumerable<YouTubeVideoId> videos)
    {
        var taken = _youTubeVideosTaken.ToHashSet(StringComparer.Ordinal);
        foreach (var video in videos)
        {
            if (taken.Add(video.Value)) _youTubeVideosTaken.Add(video.Value);
        }
    }

    /// <summary>Adds tracks from its YouTube playlist and records them as taken. Refuses all when they do not fit.</summary>
    public ValidationError? AppendFromYouTube(IReadOnlyList<(YouTubeVideoId Video, TrackId Track)> songs, DateTimeOffset addedAt)
    {
        if (Append([.. songs.Select(song => song.Track)], addedAt) is { } error) return error;
        MarkTakenFromYouTube(songs.Select(song => song.Video));
        if (songs.Count > 0) Raise(new SongsAddedFromYouTube(Id, songs.Count));
        return null;
    }

    private bool HasPosition(int position) => position >= 0 && position < _entries.Count;

    private static ValidationError? Check(string name, string? description)
    {
        var trimmed = name.Trim();
        if (trimmed.Length is 0 or > NameMaxLength)
            return ValidationError.For("name", $"Must be 1-{NameMaxLength} characters.");
        if (description?.Trim().Length > DescriptionMaxLength)
            return ValidationError.For("description", $"Must be at most {DescriptionMaxLength} characters.");
        return null;
    }

    private static string? Normalize(string? description) => string.IsNullOrWhiteSpace(description) ? null : description.Trim();
}
