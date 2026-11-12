using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OldCountry.Api.Common.Errors;
using OldCountry.Api.Common.Messaging;
using OldCountry.Api.Persistence;
using OldCountry.Domain.Common;
using OldCountry.Domain.Errors;

namespace OldCountry.Api.Features.Playlists.GetPlaylist;

public sealed record GetPlaylistQuery(PlaylistId Id) : IQuery<GetPlaylistResult>;

public readonly union GetPlaylistResult(PlaylistResponse, NotFoundError);

internal sealed class GetPlaylistHandler(OldCountryDbContext db) : IQueryHandler<GetPlaylistQuery, GetPlaylistResult>
{
    public async Task<GetPlaylistResult> Handle(GetPlaylistQuery query, CancellationToken cancellationToken)
    {
        var playlist = await db.Playlists.AsNoTracking().SingleOrDefaultAsync(p => p.Id == query.Id, cancellationToken);
        if (playlist is null) return new NotFoundError("playlist", query.Id.ToString());

        // One query for every distinct track in the playlist, then stitch them back in play order.
        var trackIds = playlist.Entries.Select(entry => entry.TrackId).Distinct().ToList();
        var tracks = await (
                from track in db.Tracks.AsNoTracking()
                join artist in db.Artists on track.ArtistId equals artist.Id
                where trackIds.Contains(track.Id)
                select new { Track = track, ArtistName = artist.Name })
            .ToDictionaryAsync(row => row.Track.Id, cancellationToken);

        var entries = new List<PlaylistEntryResponse>(playlist.Entries.Count);
        for (var position = 0; position < playlist.Entries.Count; position++)
        {
            var entry = playlist.Entries[position];
            // A track removed from the library leaves a gap; positions keep matching the stored list.
            if (!tracks.TryGetValue(entry.TrackId, out var row)) continue;
            entries.Add(new PlaylistEntryResponse(
                position, entry.TrackId.Value, row.Track.Title, row.ArtistName, row.Track.Duration, row.Track.IsAvailableOffline, entry.AddedAt));
        }

        return new PlaylistResponse(
            playlist.Id.Value,
            playlist.Name,
            playlist.Description,
            entries,
            entries.Aggregate(TimeSpan.Zero, (total, entry) => total + entry.Duration),
            playlist.CreatedAt,
            playlist.UpdatedAt);
    }
}

internal static class GetPlaylistEndpoint
{
    public const string Name = "GetPlaylist";

    public static void Map(IEndpointRouteBuilder playlists) => playlists
        .MapGet("/{id:guid}", Handle)
        .WithName(Name)
        .WithSummary("Get a playlist with its tracks in order")
        .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<Results<Ok<PlaylistResponse>, ProblemHttpResult>> Handle(
        Guid id, IQueryHandler<GetPlaylistQuery, GetPlaylistResult> handler, CancellationToken cancellationToken) =>
        await handler.Handle(new GetPlaylistQuery(new PlaylistId(id)), cancellationToken) switch
        {
            PlaylistResponse playlist => TypedResults.Ok(playlist),
            NotFoundError notFound => notFound.ToProblem(),
        };
}
