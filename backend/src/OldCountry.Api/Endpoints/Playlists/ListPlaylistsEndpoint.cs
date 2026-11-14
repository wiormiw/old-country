using Microsoft.AspNetCore.Http.HttpResults;
using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Application.Playlists;
using OldCountry.Application.Playlists.ListPlaylists;

namespace OldCountry.Api.Endpoints.Playlists;

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
