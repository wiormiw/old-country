using Microsoft.AspNetCore.Http.HttpResults;
using OldCountry.Api.Http;
using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Application.Playlists.RemovePlaylistEntry;
using OldCountry.Domain.Errors;
using OldCountry.Domain.Playlists;

namespace OldCountry.Api.Endpoints.Playlists;

internal static class RemovePlaylistEntryEndpoint
{
    public static void Map(IEndpointRouteBuilder playlists) => playlists
        .MapDelete("/{id:guid}/tracks/{position:int}", Handle)
        .WithName("RemovePlaylistEntry")
        .WithSummary("Remove the entry at a position from a playlist")
        .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<Results<NoContent, ProblemHttpResult>> Handle(
        Guid id, int position, ICommandHandler<RemovePlaylistEntryCommand, RemovePlaylistEntryResult> handler, CancellationToken cancellationToken) =>
        await handler.Handle(new RemovePlaylistEntryCommand(new PlaylistId(id), position), cancellationToken) switch
        {
            Done => TypedResults.NoContent(),
            NotFoundError notFound => notFound.ToProblem(),
        };
}
