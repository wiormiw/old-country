using OldCountry.Domain.Common;
using OldCountry.Domain.Errors;

namespace OldCountry.Domain.Library;

/// <summary>One slot in a playlist. The same track may appear more than once.</summary>
public sealed record PlaylistEntry(TrackId TrackId, DateTimeOffset AddedAt);

/// <summary>
/// An ordered list of tracks. The entries are a value collection owned by the playlist: they
/// have no identity of their own, only a position, and change only through these methods.
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
    /// The YouTube Music playlist this one was imported from (its list id, e.g. <c>PL…</c>, or
    /// <c>LM</c> for liked songs), or <c>null</c> for a playlist made here. Importing that
    /// playlist again updates this one instead of making another.
    /// </summary>
    public string? YouTubeListId { get; private set; }

    /// <summary>
    /// Every video this playlist has taken from its YouTube playlist. A sync adds only videos not
    /// in here, so a song removed from this playlist stays removed even while it's still on YouTube.
    /// </summary>
    private readonly List<string> _youTubeVideosTaken = [];

    public static Validated<Playlist> Create(string name, string? description) =>
        Check(name, description) switch
        {
            { } error => error,
            null => new Playlist { Id = PlaylistId.New(), Name = name.Trim(), Description = Normalize(description) },
        };

    /// <summary>Renames and re-describes the playlist, or says why not.</summary>
    public ValidationError? Rename(string name, string? description)
    {
        if (Check(name, description) is { } error) return error;
        Name = name.Trim();
        Description = Normalize(description);
        return null;
    }

    /// <summary>Appends tracks in the given order, or refuses all of them if the playlist would grow too long.</summary>
    public ValidationError? Append(IReadOnlyCollection<TrackId> trackIds, DateTimeOffset addedAt)
    {
        if (_entries.Count + trackIds.Count > MaxEntries)
            return ValidationError.For("trackIds", $"A playlist holds at most {MaxEntries} tracks.");
        _entries.AddRange(trackIds.Select(id => new PlaylistEntry(id, addedAt)));
        return null;
    }

    /// <summary><c>false</c> when there is no entry at that position.</summary>
    public bool RemoveAt(int position)
    {
        if (!HasPosition(position)) return false;
        _entries.RemoveAt(position);
        return true;
    }

    /// <summary>Moves one entry; the others shift to make room. <c>false</c> when either position is out of range.</summary>
    public bool Move(int from, int to)
    {
        if (!HasPosition(from) || !HasPosition(to)) return false;
        var entry = _entries[from];
        _entries.RemoveAt(from);
        _entries.Insert(to, entry);
        return true;
    }

    /// <summary>Remembers which YouTube playlist this one comes from, so importing it again syncs it.</summary>
    public ValidationError? LinkToYouTube(string listId)
    {
        if (string.IsNullOrWhiteSpace(listId) || listId.Length > YouTubeListIdMaxLength)
            return ValidationError.For("youTubeListId", $"Must be 1-{YouTubeListIdMaxLength} characters.");
        YouTubeListId = listId;
        return null;
    }

    /// <summary>
    /// The videos of a YouTube playlist (in YouTube's order) that a sync would add: ones this
    /// playlist hasn't taken before and doesn't already hold (<paramref name="present"/>), each once.
    /// </summary>
    public IReadOnlyList<YouTubeVideoId> NewOnYouTube(IEnumerable<YouTubeVideoId> source, IReadOnlySet<YouTubeVideoId> present)
    {
        var taken = _youTubeVideosTaken.ToHashSet(StringComparer.Ordinal);
        return [.. source.Distinct().Where(video => !taken.Contains(video.Value) && !present.Contains(video))];
    }

    /// <summary>Records videos as taken from YouTube without adding them (they're already here).</summary>
    public void MarkTakenFromYouTube(IEnumerable<YouTubeVideoId> videos)
    {
        var taken = _youTubeVideosTaken.ToHashSet(StringComparer.Ordinal);
        foreach (var video in videos)
        {
            if (taken.Add(video.Value)) _youTubeVideosTaken.Add(video.Value);
        }
    }

    /// <summary>Appends songs from its YouTube playlist and records them as taken, or refuses all if they don't fit.</summary>
    public ValidationError? AppendFromYouTube(IReadOnlyList<(YouTubeVideoId Video, TrackId Track)> songs, DateTimeOffset addedAt)
    {
        if (Append([.. songs.Select(song => song.Track)], addedAt) is { } error) return error;
        MarkTakenFromYouTube(songs.Select(song => song.Video));
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
