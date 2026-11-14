using OldCountry.Domain.Common;

namespace OldCountry.Domain.Playlists;

/// <summary>An import or a sync added tracks from a YouTube Music playlist to its playlist here.</summary>
public sealed record SongsAddedFromYouTube(PlaylistId Playlist, int Count) : IDomainEvent;
