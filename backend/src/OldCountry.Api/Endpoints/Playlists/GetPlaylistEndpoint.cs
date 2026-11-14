using Microsoft.AspNetCore.Http.HttpResults;
using OldCountry.Api.Http;
using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Application.Playlists;
using OldCountry.Application.Playlists.GetPlaylist;
using OldCountry.Domain.Errors;
using OldCountry.Domain.Playlists;

namespace OldCountry.Api.Endpoints.Playlists;

internal static class GetPlaylistEndpoint
{
    public const string Name = "GetPlaylist";

    public static void Map(IEndpointRouteBuilder playlists) => playlists
        .MapGet("/{id:guid}", Handle)
        .WithName(Name)
        .WithSummary("Get a playlist with its tracks in order")
        .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<Results<Ok<PlaylistResponse>, ProblemHttpResult>> Handle(
        Guid id, IQueryHandler<GetPlaylistQuery, GetPlaylistResult> handler, CancellationToken cancellationToken) =>
        await handler.Handle(new GetPlaylistQuery(new PlaylistId(id)), cancellationToken) switch
        {
            PlaylistResponse playlist => TypedResults.Ok(playlist),
            NotFoundError notFound => notFound.ToProblem(),
        };
}
