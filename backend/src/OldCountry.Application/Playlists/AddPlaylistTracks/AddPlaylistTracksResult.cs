using OldCountry.Domain.Errors;

namespace OldCountry.Application.Playlists.AddPlaylistTracks;

public readonly union AddPlaylistTracksResult(PlaylistSummaryResponse, NotFoundError, ValidationError);
