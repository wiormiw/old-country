using OldCountry.Application.Abstractions.Messaging;

namespace OldCountry.Application.Playlists.ListPlaylists;

public sealed record ListPlaylistsQuery : IQuery<IReadOnlyList<PlaylistSummaryResponse>>;
