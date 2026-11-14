using OldCountry.Domain.Errors;
using OldCountry.Domain.Tracks;

namespace OldCountry.Application.Downloads.DownloadTrack;

public readonly union DownloadTrackResult(TrackId, ValidationError, ExternalServiceError);
