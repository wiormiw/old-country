using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using OldCountry.Api.Http;
using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Application.Playlists;
using OldCountry.Application.Playlists.AddPlaylistTracks;
using OldCountry.Domain.Errors;
using OldCountry.Domain.Playlists;
using OldCountry.Domain.Tracks;

namespace OldCountry.Api.Endpoints.Playlists;

internal static class AddPlaylistTracksEndpoint
{
    public static void Map(IEndpointRouteBuilder playlists) => playlists
        .MapPost("/{id:guid}/tracks", Handle)
        .Validate<AddPlaylistTracksRequest>()
        .WithName("AddPlaylistTracks")
        .WithSummary("Append tracks to a playlist")
        .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<Results<Ok<PlaylistSummaryResponse>, ProblemHttpResult, ValidationProblem>> Handle(
        Guid id, AddPlaylistTracksRequest request, ICommandHandler<AddPlaylistTracksCommand, AddPlaylistTracksResult> handler,
        CancellationToken cancellationToken) =>
        await handler.Handle(new AddPlaylistTracksCommand(new PlaylistId(id), [.. request.TrackIds.Select(t => new TrackId(t))]), cancellationToken) switch
        {
            PlaylistSummaryResponse playlist => TypedResults.Ok(playlist),
            NotFoundError notFound => notFound.ToProblem(),
            ValidationError invalid => invalid.ToProblem(),
        };
}
