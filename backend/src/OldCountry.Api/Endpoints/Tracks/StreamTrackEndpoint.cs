using Microsoft.AspNetCore.Http.HttpResults;
using OldCountry.Api.Http;
using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Application.Tracks.StreamTrack;
using OldCountry.Domain.Errors;
using OldCountry.Domain.Tracks;

namespace OldCountry.Api.Endpoints.Tracks;

internal static class StreamTrackEndpoint
{
    public static void Map(IEndpointRouteBuilder tracks) => tracks
        .MapGet("/{id:guid}/audio", Handle)
        .WithName("StreamTrack")
        .WithSummary("Stream a track's local audio file")
        .WithDescription("Supports HTTP range requests, so players can seek. 404 when the track has no playable local file.")
        .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<Results<PhysicalFileHttpResult, ProblemHttpResult>> Handle(
        Guid id, IQueryHandler<StreamTrackQuery, StreamTrackResult> handler, CancellationToken cancellationToken) =>
        await handler.Handle(new StreamTrackQuery(new TrackId(id)), cancellationToken) switch
        {
            LocalAudio audio => TypedResults.PhysicalFile(audio.Path, audio.ContentType, enableRangeProcessing: true),
            NotFoundError notFound => notFound.ToProblem(),
        };
}
