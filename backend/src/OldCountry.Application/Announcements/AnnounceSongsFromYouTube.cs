using Microsoft.EntityFrameworkCore;
using OldCountry.Application.Abstractions.Persistence;
using OldCountry.Application.Events;
using OldCountry.Application.Realtime;
using OldCountry.Domain.Playlists;

namespace OldCountry.Application.Announcements;

internal sealed class AnnounceSongsFromYouTube(IAppDbContext db, ChangeFeed feed) : IDomainEventHandler<SongsAddedFromYouTube>
{
    public async Task Handle(SongsAddedFromYouTube added, CancellationToken cancellationToken)
    {
        var name = await db.Playlists.Where(p => p.Id == added.Playlist).Select(p => p.Name).FirstOrDefaultAsync(cancellationToken);
        if (name is null) return; // The playlist was deleted after the event.
        var songs = added.Count == 1 ? "1 new song" : $"{added.Count} new songs";
        feed.Announce(new Announcement(AnnouncementKind.NewFromYouTube, $"“{name}”: {songs} from YouTube.", PlaylistId: added.Playlist.Value));
    }
}
