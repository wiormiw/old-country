using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Application.Abstractions.Persistence;
using OldCountry.Application.Common.Library;
using OldCountry.Application.Common.YouTube;
using OldCountry.Domain.Downloads;
using OldCountry.Domain.Errors;
using OldCountry.Domain.Tracks;
using OldCountry.Domain.YouTube;

namespace OldCountry.Application.Downloads.QueuePlaylistDownloads;

internal sealed class QueuePlaylistDownloadsHandler(
    IAppDbContext db,
    DownloadDoorbell doorbell,
    TimeProvider clock,
    IOptions<YouTubeMusicOptions> youTube,
    IOptions<LibraryOptions> library) : ICommandHandler<QueuePlaylistDownloadsCommand, QueuePlaylistDownloadsResult>
{
    public async Task<QueuePlaylistDownloadsResult> Handle(QueuePlaylistDownloadsCommand command, CancellationToken cancellationToken)
    {
        var playlist = await db.Playlists.AsNoTracking().SingleOrDefaultAsync(p => p.Id == command.PlaylistId, cancellationToken);
        if (playlist is null) return new NotFoundError("playlist", command.PlaylistId.ToString());
        if (youTube.Value.ResolveDownloadFolder(library.Value).Value is ValidationError invalid) return invalid;

        // Each track one time, in the order of the playlist.
        var trackIds = playlist.Entries.Select(entry => entry.TrackId).Distinct().ToList();
        var tracks = await db.Tracks.AsNoTracking().Where(t => trackIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, cancellationToken);
        int downloaded = 0, skipped = 0, notOnYouTube = 0;
        var wanted = new List<YouTubeVideoId>();
        foreach (var track in trackIds.Select(tracks.GetValueOrDefault).OfType<Track>())
        {
            if (track.IsAvailableOffline) downloaded++;
            else if (track.YouTubeVideoId is not { } video) notOnYouTube++;
            else if (track.DownloadProblem is { IsLasting: true } && !command.RetrySkipped) skipped++;
            else wanted.Add(video);
        }

        var alreadyQueued = await db.DownloadJobs
            .Where(j => wanted.Contains(j.VideoId) && (j.State == DownloadJobState.Queued || j.State == DownloadJobState.Running))
            .CountAsync(cancellationToken);
        if (wanted.Count > 0) await db.QueueAsync(wanted, doorbell, clock.GetUtcNow(), cancellationToken);

        return new PlaylistDownloadReport(wanted.Count - alreadyQueued, downloaded, alreadyQueued, skipped, notOnYouTube);
    }
}
