using OldCountry.Domain.Errors;

namespace OldCountry.Application.Playlists.GetPlaylist;

public readonly union GetPlaylistResult(PlaylistResponse, NotFoundError);
