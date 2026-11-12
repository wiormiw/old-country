using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OldCountry.Api.Common.Errors;
using OldCountry.Api.Common.Messaging;
using OldCountry.Api.Persistence;
using OldCountry.Domain.Common;
using OldCountry.Domain.Errors;

namespace OldCountry.Api.Features.Tracks.GetTrack;

public sealed record GetTrackQuery(TrackId Id) : IQuery<GetTrackResult>;

/// <summary>Every way this use case can end. The endpoint's switch must handle each case.</summary>
public readonly union GetTrackResult(TrackResponse, NotFoundError);

internal sealed class GetTrackHandler(OldCountryDbContext db) : IQueryHandler<GetTrackQuery, GetTrackResult>
{
    public async Task<GetTrackResult> Handle(GetTrackQuery query, CancellationToken cancellationToken)
    {
        var row = await (
                from track in db.Tracks.AsNoTracking()
                join artist in db.Artists on track.ArtistId equals artist.Id
                where track.Id == query.Id
                select new { Track = track, ArtistName = artist.Name })
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null) return new NotFoundError("track", query.Id.ToString());
        return row.Track.ToResponse(row.ArtistName);
    }
}

internal static class GetTrackEndpoint
{
    public const string Name = "GetTrack";

    public static void Map(IEndpointRouteBuilder tracks) => tracks
        .MapGet("/{id:guid}", Handle)
        .WithName(Name)
        .WithSummary("Get a track by id")
        .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<Results<Ok<TrackResponse>, ProblemHttpResult>> Handle(
        Guid id, IQueryHandler<GetTrackQuery, GetTrackResult> handler, CancellationToken cancellationToken) =>
        await handler.Handle(new GetTrackQuery(new TrackId(id)), cancellationToken) switch
        {
            TrackResponse track => TypedResults.Ok(track),
            NotFoundError notFound => notFound.ToProblem(),
        };
}
