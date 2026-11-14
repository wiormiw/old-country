using Microsoft.EntityFrameworkCore;
using OldCountry.Application.Abstractions.Persistence;
using OldCountry.Application.Events;
using OldCountry.Application.Realtime;
using OldCountry.Domain.Downloads;

namespace OldCountry.Application.Announcements;

internal sealed class AnnounceDownloadFailed(IAppDbContext db, ChangeFeed feed) : IDomainEventHandler<DownloadFailed>
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

    /// <summary>The same short reasons that the apps show next to a refused track.</summary>
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
