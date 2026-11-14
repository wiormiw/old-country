using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using OldCountry.Api.Http;
using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Application.Playlists.MovePlaylistEntry;
using OldCountry.Domain.Errors;
using OldCountry.Domain.Playlists;

namespace OldCountry.Api.Endpoints.Playlists;

internal static class MovePlaylistEntryEndpoint
{
    public static void Map(IEndpointRouteBuilder playlists) => playlists
        .MapPost("/{id:guid}/tracks/move", Handle)
        .Validate<MovePlaylistEntryRequest>()
        .WithName("MovePlaylistEntry")
        .WithSummary("Reorder a playlist by moving one entry")
        .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<Results<NoContent, ProblemHttpResult>> Handle(
        Guid id, MovePlaylistEntryRequest request, ICommandHandler<MovePlaylistEntryCommand, MovePlaylistEntryResult> handler,
        CancellationToken cancellationToken) =>
        await handler.Handle(new MovePlaylistEntryCommand(new PlaylistId(id), request.From, request.To), cancellationToken) switch
        {
            Done => TypedResults.NoContent(),
            NotFoundError notFound => notFound.ToProblem(),
        };
}
