using Microsoft.EntityFrameworkCore;
using OldCountry.Api.Common.Events;
using OldCountry.Api.Persistence;
using OldCountry.Domain.Library;

namespace OldCountry.Api.Common.Realtime;

// Domain events → news for the apps. Each runs after the change committed, in its own scope, and
// looks up the names people know things by (events carry only ids).

internal sealed class AnnounceDownloadFinished(OldCountryDbContext db, ChangeFeed feed) : IDomainEventHandler<DownloadFinished>
{
    public async Task Handle(DownloadFinished downloaded, CancellationToken cancellationToken)
    {
        var title = await db.Tracks.Where(t => t.Id == downloaded.Track).Select(t => t.Title).FirstOrDefaultAsync(cancellationToken);
        feed.Announce(new Announcement(
            AnnouncementKind.Downloaded,
            title is null ? "A download finished." : $"Downloaded “{title}”.",
            TrackId: downloaded.Track.Value));
    }
}

internal sealed class AnnounceDownloadFailed(OldCountryDbContext db, ChangeFeed feed) : IDomainEventHandler<DownloadFailed>
{
    public async Task Handle(DownloadFailed failed, CancellationToken cancellationToken)
    {
        var track = await db.Tracks.Where(t => t.YouTubeVideoId == failed.Video)
            .Select(t => new { t.Id, t.Title }).FirstOrDefaultAsync(cancellationToken);
        var song = track is null ? "a song" : $"“{track.Title}”";
        var message = Refusal(failed.Code) is { } reason
            ? $"YouTube refused {song}: {reason}."
            : $"Couldn't download {song}. See Downloads.";
        feed.Announce(new Announcement(AnnouncementKind.DownloadFailed, message, TrackId: track?.Id.Value));
    }

    /// <summary>The same short reasons the apps show next to a refused song.</summary>
    private static string? Refusal(string code) => code switch
    {
        "youtube.premium_only" => "Premium only",
        "youtube.private" => "private video",
        "youtube.unavailable" => "removed",
        "youtube.age_restricted" => "age restricted",
        "youtube.region_blocked" => "not available in your country",
        _ => null,
    };
}

internal sealed class AnnounceSongsFromYouTube(OldCountryDbContext db, ChangeFeed feed) : IDomainEventHandler<SongsAddedFromYouTube>
{
    public async Task Handle(SongsAddedFromYouTube added, CancellationToken cancellationToken)
    {
        var name = await db.Playlists.Where(p => p.Id == added.Playlist).Select(p => p.Name).FirstOrDefaultAsync(cancellationToken);
        if (name is null) return; // deleted since
        var songs = added.Count == 1 ? "1 new song" : $"{added.Count} new songs";
        feed.Announce(new Announcement(AnnouncementKind.NewFromYouTube, $"“{name}”: {songs} from YouTube.", PlaylistId: added.Playlist.Value));
    }
}
