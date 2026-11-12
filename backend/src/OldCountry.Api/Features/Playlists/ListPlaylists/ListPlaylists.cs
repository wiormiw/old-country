using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OldCountry.Api.Common.Messaging;
using OldCountry.Api.Persistence;

namespace OldCountry.Api.Features.Playlists.ListPlaylists;

public sealed record ListPlaylistsQuery : IQuery<IReadOnlyList<PlaylistSummaryResponse>>;

internal sealed class ListPlaylistsHandler(OldCountryDbContext db) : IQueryHandler<ListPlaylistsQuery, IReadOnlyList<PlaylistSummaryResponse>>
{
    public async Task<IReadOnlyList<PlaylistSummaryResponse>> Handle(ListPlaylistsQuery query, CancellationToken cancellationToken)
    {
        // A personal library has tens of playlists, not thousands: no paging, counts taken in memory.
        var playlists = await db.Playlists.AsNoTracking().OrderBy(p => p.Name).ThenBy(p => p.Id).ToListAsync(cancellationToken);
        return [.. playlists.Select(playlist => playlist.ToSummary())];
    }
}

internal static class ListPlaylistsEndpoint
{
    public static void Map(IEndpointRouteBuilder playlists) => playlists
        .MapGet("/", Handle)
        .WithName("ListPlaylists")
        .WithSummary("List all playlists by name");

    private static async Task<Ok<IReadOnlyList<PlaylistSummaryResponse>>> Handle(
        IQueryHandler<ListPlaylistsQuery, IReadOnlyList<PlaylistSummaryResponse>> handler, CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.Handle(new ListPlaylistsQuery(), cancellationToken));
}
