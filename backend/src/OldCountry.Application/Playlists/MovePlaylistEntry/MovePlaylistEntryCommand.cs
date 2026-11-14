using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Domain.Playlists;

namespace OldCountry.Application.Playlists.MovePlaylistEntry;

public sealed record MovePlaylistEntryCommand(PlaylistId Id, int From, int To) : ICommand<MovePlaylistEntryResult>;
