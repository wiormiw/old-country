using OldCountry.Domain.Errors;

namespace OldCountry.Application.Tracks.AddTrack;

public readonly union AddTrackResult(TrackResponse, ValidationError, ConflictError);
