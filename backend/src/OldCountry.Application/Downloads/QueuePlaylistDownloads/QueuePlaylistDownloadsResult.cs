using OldCountry.Domain.Errors;

namespace OldCountry.Application.Downloads.QueuePlaylistDownloads;

public readonly union QueuePlaylistDownloadsResult(PlaylistDownloadReport, NotFoundError, ValidationError);
