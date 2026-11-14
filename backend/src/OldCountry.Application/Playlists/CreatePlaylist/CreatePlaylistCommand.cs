using OldCountry.Application.Abstractions.Messaging;

namespace OldCountry.Application.Playlists.CreatePlaylist;

public sealed record CreatePlaylistCommand(string Name, string? Description) : ICommand<CreatePlaylistResult>;
