using Microsoft.EntityFrameworkCore;
using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Application.Abstractions.Persistence;
using OldCountry.Domain.Errors;

namespace OldCountry.Application.Playlists.GetPlaylist;

internal sealed class GetPlaylistHandler(IAppDbContext db) : IQueryHandler<GetPlaylistQuery, GetPlaylistResult>
{
    public async Task<GetPlaylistResult> Handle(GetPlaylistQuery query, CancellationToken cancellationToken)
    {
        var playlist = await db.Playlists.AsNoTracking().SingleOrDefaultAsync(p => p.Id == query.Id, cancellationToken);
        if (playlist is null) return new NotFoundError("playlist", query.Id.ToString());

        // One query for all the different tracks of the playlist. Then put them back in the play order.
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
            // A track that was removed from the library leaves a gap. The positions continue to match the stored list.
            if (!tracks.TryGetValue(entry.TrackId, out var row)) continue;
            entries.Add(new PlaylistEntryResponse(
                position, entry.TrackId.Value, row.Track.Title, row.ArtistName, row.Track.Duration, row.Track.IsAvailableOffline,
                row.Track.YouTubeVideoId?.Value, entry.AddedAt, row.Track.DownloadProblem?.Reason));
        }

        return new PlaylistResponse(
            playlist.Id.Value,
            playlist.Name,
            playlist.Description,
            entries,
            entries.Aggregate(TimeSpan.Zero, (total, entry) => total + entry.Duration),
            playlist.CreatedAt,
            playlist.UpdatedAt,
            playlist.YouTubeListId,
            playlist.YouTubeSyncedAt);
    }
}
