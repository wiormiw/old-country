using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Domain.Errors;

namespace OldCountry.Application.Playlists.DeletePlaylist;

public readonly union DeletePlaylistResult(Done, NotFoundError);
