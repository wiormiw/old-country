using Microsoft.EntityFrameworkCore;
using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Application.Abstractions.Persistence;

namespace OldCountry.Application.Playlists.ListPlaylists;

internal sealed class ListPlaylistsHandler(IAppDbContext db) : IQueryHandler<ListPlaylistsQuery, IReadOnlyList<PlaylistSummaryResponse>>
{
    public async Task<IReadOnlyList<PlaylistSummaryResponse>> Handle(ListPlaylistsQuery query, CancellationToken cancellationToken)
    {
        // A personal library has tens of playlists, not thousands. Thus there is no paging, and the counts are made in memory.
        var playlists = await db.Playlists.AsNoTracking().OrderBy(p => p.Name).ThenBy(p => p.Id).ToListAsync(cancellationToken);
        return [.. playlists.Select(playlist => playlist.ToSummary())];
    }
}
