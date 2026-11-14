namespace OldCountry.Application.Playlists.MovePlaylistEntry;

/// <summary>Moves one entry. The entries between the two positions shift by one.</summary>
public sealed record MovePlaylistEntryRequest
{
    /// <summary>The current zero-based position of the entry.</summary>
    public required int From { get; init; }

    /// <summary>The zero-based position where the entry must go.</summary>
    public required int To { get; init; }
}
