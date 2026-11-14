using OldCountry.Domain.Tracks;

namespace OldCountry.Domain.Playlists;

/// <summary>One position in a playlist. The same track can occur more than one time.</summary>
public sealed record PlaylistEntry(TrackId TrackId, DateTimeOffset AddedAt);
