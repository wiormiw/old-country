using Microsoft.AspNetCore.Http.HttpResults;
using OldCountry.Api.Http;
using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Application.Tracks.GetTrack;
using OldCountry.Domain.Errors;
using OldCountry.Domain.Tracks;

namespace OldCountry.Api.Endpoints.Tracks;

internal static class GetTrackEndpoint
{
    public const string Name = "GetTrack";

    public static void Map(IEndpointRouteBuilder tracks) => tracks
        .MapGet("/{id:guid}", Handle)
        .WithName(Name)
        .WithSummary("Get a track with its file details and the playlists it's in")
        .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<Results<Ok<TrackDetailResponse>, ProblemHttpResult>> Handle(
        Guid id, IQueryHandler<GetTrackQuery, GetTrackResult> handler, CancellationToken cancellationToken) =>
        await handler.Handle(new GetTrackQuery(new TrackId(id)), cancellationToken) switch
        {
            TrackDetailResponse track => TypedResults.Ok(track),
            NotFoundError notFound => notFound.ToProblem(),
        };
}
