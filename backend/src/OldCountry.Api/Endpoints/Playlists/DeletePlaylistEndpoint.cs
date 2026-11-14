using Microsoft.AspNetCore.Http.HttpResults;
using OldCountry.Api.Http;
using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Application.Playlists.DeletePlaylist;
using OldCountry.Domain.Errors;
using OldCountry.Domain.Playlists;

namespace OldCountry.Api.Endpoints.Playlists;

internal static class DeletePlaylistEndpoint
{
    public static void Map(IEndpointRouteBuilder playlists) => playlists
        .MapDelete("/{id:guid}", Handle)
        .WithName("DeletePlaylist")
        .WithSummary("Delete a playlist (its tracks stay in the library)")
        .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<Results<NoContent, ProblemHttpResult>> Handle(
        Guid id, ICommandHandler<DeletePlaylistCommand, DeletePlaylistResult> handler, CancellationToken cancellationToken) =>
        await handler.Handle(new DeletePlaylistCommand(new PlaylistId(id)), cancellationToken) switch
        {
            Done => TypedResults.NoContent(),
            NotFoundError notFound => notFound.ToProblem(),
        };
}
