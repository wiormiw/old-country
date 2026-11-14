using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Domain.Playlists;

namespace OldCountry.Application.Playlists.GetPlaylist;

public sealed record GetPlaylistQuery(PlaylistId Id) : IQuery<GetPlaylistResult>;
