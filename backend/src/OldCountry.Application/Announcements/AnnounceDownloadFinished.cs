using Microsoft.EntityFrameworkCore;
using OldCountry.Application.Abstractions.Persistence;
using OldCountry.Application.Events;
using OldCountry.Application.Realtime;
using OldCountry.Domain.Downloads;

namespace OldCountry.Application.Announcements;

// These handlers change domain events into news for the apps. Each one runs after the commit, in its own scope.
// The events have only ids, so each handler gets the names that people know.

internal sealed class AnnounceDownloadFinished(IAppDbContext db, ChangeFeed feed) : IDomainEventHandler<DownloadFinished>
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
