using OldCountry.Domain.Errors;

namespace OldCountry.Application.Playlists.CreatePlaylist;

public readonly union CreatePlaylistResult(PlaylistSummaryResponse, ValidationError);
