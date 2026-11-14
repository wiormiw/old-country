using OldCountry.Domain.Errors;

namespace OldCountry.Application.Playlists.UpdatePlaylist;

public readonly union UpdatePlaylistResult(PlaylistSummaryResponse, NotFoundError, ValidationError);
