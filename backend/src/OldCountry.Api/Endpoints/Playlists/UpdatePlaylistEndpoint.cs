using Microsoft.AspNetCore.Http.HttpResults;
using OldCountry.Api.Http;
using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Application.Playlists;
using OldCountry.Application.Playlists.UpdatePlaylist;
using OldCountry.Domain.Errors;
using OldCountry.Domain.Playlists;

namespace OldCountry.Api.Endpoints.Playlists;

internal static class UpdatePlaylistEndpoint
{
    public static void Map(IEndpointRouteBuilder playlists) => playlists
        .MapPut("/{id:guid}", Handle)
        .Validate<PlaylistDetailsRequest>()
        .WithName("UpdatePlaylist")
        .WithSummary("Rename a playlist or change its description")
        .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<Results<Ok<PlaylistSummaryResponse>, ProblemHttpResult, ValidationProblem>> Handle(
        Guid id, PlaylistDetailsRequest request, ICommandHandler<UpdatePlaylistCommand, UpdatePlaylistResult> handler, CancellationToken cancellationToken) =>
        await handler.Handle(new UpdatePlaylistCommand(new PlaylistId(id), request.Name, request.Description), cancellationToken) switch
        {
            PlaylistSummaryResponse playlist => TypedResults.Ok(playlist),
            NotFoundError notFound => notFound.ToProblem(),
            ValidationError invalid => invalid.ToProblem(),
        };
}
