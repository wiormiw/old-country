using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using OldCountry.Api.Http;
using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Application.Tracks;
using OldCountry.Application.Tracks.AddTrack;
using OldCountry.Domain.Errors;

namespace OldCountry.Api.Endpoints.Tracks;

internal static class AddTrackEndpoint
{
    public static void Map(IEndpointRouteBuilder tracks) => tracks
        .MapPost("/", Handle)
        .Validate<AddTrackRequest>()
        .WithName("AddTrack")
        .WithSummary("Add a track to the library")
        .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<Results<CreatedAtRoute<TrackResponse>, ValidationProblem, ProblemHttpResult>> Handle(
        AddTrackRequest request, ICommandHandler<AddTrackCommand, AddTrackResult> handler, CancellationToken cancellationToken) =>
        await handler.Handle(new AddTrackCommand(request), cancellationToken) switch
        {
            TrackResponse track => TypedResults.CreatedAtRoute(track, GetTrackEndpoint.Name, new { id = track.Id }),
            ValidationError invalid => invalid.ToProblem(),
            ConflictError conflict => conflict.ToProblem(),
        };
}
