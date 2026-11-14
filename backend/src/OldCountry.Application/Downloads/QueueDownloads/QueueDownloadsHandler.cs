using Microsoft.Extensions.Options;
using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Application.Abstractions.Persistence;
using OldCountry.Application.Common.Library;
using OldCountry.Application.Common.YouTube;
using OldCountry.Domain.Errors;

namespace OldCountry.Application.Downloads.QueueDownloads;

internal sealed class QueueDownloadsHandler(
    IAppDbContext db,
    DownloadDoorbell doorbell,
    TimeProvider clock,
    IOptions<YouTubeMusicOptions> youTube,
    IOptions<LibraryOptions> library) : ICommandHandler<QueueDownloadsCommand, QueueDownloadsResult>
{
    public async Task<QueueDownloadsResult> Handle(QueueDownloadsCommand command, CancellationToken cancellationToken)
    {
        // Refuse at the start when there is no folder for the files. Do not let each job fail.
        if (youTube.Value.ResolveDownloadFolder(library.Value).Value is ValidationError invalid) return invalid;

        var jobs = await db.QueueAsync(command.VideoIds, doorbell, clock.GetUtcNow(), cancellationToken);
        var titles = await db.TitlesAsync(command.VideoIds, cancellationToken);
        IReadOnlyList<DownloadStatus> statuses = [.. jobs.Select(job => job.ToStatus(titles.GetValueOrDefault(job.VideoId)))];
        return statuses;
    }
}
