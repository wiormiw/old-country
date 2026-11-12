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

    private readonly List<PlaylistEntry> _entries = [];

    private Playlist() { }

    public string Name { get; private set; } = null!;

    public string? Description { get; private set; }

    public IReadOnlyList<PlaylistEntry> Entries => _entries;

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
