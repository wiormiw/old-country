using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OldCountry.Api.Common.Errors;
using OldCountry.Api.Common.Messaging;
using OldCountry.Api.Persistence;
using OldCountry.Domain.Common;
using OldCountry.Domain.Errors;

namespace OldCountry.Api.Features.Tracks.GetTrack;

public sealed record GetTrackQuery(TrackId Id) : IQuery<GetTrackResult>;

/// <summary>A playlist a track appears in.</summary>
/// <param name="Id">The playlist's id.</param>
/// <param name="Name">Playlist name.</param>
public sealed record PlaylistRef(Guid Id, string Name);

/// <summary>Everything about one track, for its detail page.</summary>
/// <param name="Track">The track as lists show it.</param>
/// <param name="FileName">The local file's name (not its full path), when it has one.</param>
/// <param name="SizeBytes">Size of the local file.</param>
/// <param name="BitrateKbps">Bitrate of the local file, 0 when unknown.</param>
/// <param name="SampleRateHz">Sample rate of the local file, 0 when unknown.</param>
/// <param name="Playlists">Playlists that contain the track.</param>
public sealed record TrackDetailResponse(
    TrackResponse Track,
    string? FileName,
    long? SizeBytes,
    int? BitrateKbps,
    int? SampleRateHz,
    IReadOnlyList<PlaylistRef> Playlists);

/// <summary>Every way this use case can end. The endpoint's switch must handle each case.</summary>
public readonly union GetTrackResult(TrackDetailResponse, NotFoundError);

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

        // Entries live in each playlist's jsonb column; a personal library has few playlists, so
        // they're checked in memory.
        var playlists = await db.Playlists.AsNoTracking().OrderBy(p => p.Name).ToListAsync(cancellationToken);
        var containing = playlists
            .Where(p => p.Entries.Any(entry => entry.TrackId == query.Id))
            .Select(p => new PlaylistRef(p.Id.Value, p.Name))
            .ToList();

        var file = row.Track.LocalFile;
        return new TrackDetailResponse(
            row.Track.ToResponse(row.ArtistName),
            file is null ? null : Path.GetFileName(file.Path),
            file?.SizeBytes,
            file?.Quality.BitrateKbps,
            file?.Quality.SampleRateHz,
            containing);
    }
}

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
