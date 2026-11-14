using Microsoft.EntityFrameworkCore;
using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Application.Abstractions.Persistence;
using OldCountry.Application.Common.YouTube;

namespace OldCountry.Application.Downloads.ListDownloads;

internal sealed class ListDownloadsHandler(IAppDbContext db, YouTubeRateLimit pauses, TimeProvider clock)
    : IQueryHandler<ListDownloadsQuery, DownloadsResponse>
{
    private const int Shown = 200;

    public async Task<DownloadsResponse> Handle(ListDownloadsQuery query, CancellationToken cancellationToken)
    {
        var jobs = await db.DownloadJobs.AsNoTracking().OrderByDescending(j => j.QueuedAt).Take(Shown).ToListAsync(cancellationToken);
        var titles = await db.TitlesAsync(jobs.Select(j => j.VideoId), cancellationToken);
        var paused = pauses.Remaining(clock.GetUtcNow()) is not null;
        return new DownloadsResponse(
            [.. jobs.Select(job => job.ToStatus(titles.GetValueOrDefault(job.VideoId)))],
            paused ? pauses.Until : null,
            paused ? pauses.Reason : null);
    }
}
