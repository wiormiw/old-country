using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Domain.Playlists;

namespace OldCountry.Application.Playlists.DeletePlaylist;

public sealed record DeletePlaylistCommand(PlaylistId Id) : ICommand<DeletePlaylistResult>;
