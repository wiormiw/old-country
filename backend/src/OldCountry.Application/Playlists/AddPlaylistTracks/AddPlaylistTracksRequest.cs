namespace OldCountry.Application.Playlists.AddPlaylistTracks;

/// <summary>Tracks to add at the end of a playlist, in this order.</summary>
public sealed record AddPlaylistTracksRequest
{
    /// <summary>The ids of library tracks, 1-500 for each request. Duplicates are allowed and kept.</summary>
    public required IReadOnlyList<Guid> TrackIds { get; init; }
}
