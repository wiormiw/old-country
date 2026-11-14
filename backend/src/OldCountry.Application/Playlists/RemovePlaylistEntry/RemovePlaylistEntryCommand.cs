using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Domain.Playlists;

namespace OldCountry.Application.Playlists.RemovePlaylistEntry;

/// <summary>Removes the entry at a position. Other entries of the same track stay.</summary>
public sealed record RemovePlaylistEntryCommand(PlaylistId Id, int Position) : ICommand<RemovePlaylistEntryResult>;
