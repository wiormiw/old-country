using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Domain.Playlists;
using OldCountry.Domain.Tracks;

namespace OldCountry.Application.Playlists.AddPlaylistTracks;

public sealed record AddPlaylistTracksCommand(PlaylistId Id, IReadOnlyList<TrackId> TrackIds) : ICommand<AddPlaylistTracksResult>;
