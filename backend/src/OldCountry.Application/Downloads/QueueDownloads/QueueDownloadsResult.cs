using OldCountry.Domain.Errors;

namespace OldCountry.Application.Downloads.QueueDownloads;

public readonly union QueueDownloadsResult(IReadOnlyList<DownloadStatus>, ValidationError);
