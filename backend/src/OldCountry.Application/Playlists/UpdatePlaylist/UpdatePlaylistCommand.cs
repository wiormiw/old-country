using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Domain.Playlists;

namespace OldCountry.Application.Playlists.UpdatePlaylist;

public sealed record UpdatePlaylistCommand(PlaylistId Id, string Name, string? Description) : ICommand<UpdatePlaylistResult>;
