using Microsoft.EntityFrameworkCore;
using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Application.Abstractions.Persistence;
using OldCountry.Domain.Errors;

namespace OldCountry.Application.Tracks.GetTrack;

internal sealed class GetTrackHandler(IAppDbContext db) : IQueryHandler<GetTrackQuery, GetTrackResult>
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

        // The entries are in the jsonb column of each playlist. A personal library has few playlists,
        // so the handler checks them in memory.
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
