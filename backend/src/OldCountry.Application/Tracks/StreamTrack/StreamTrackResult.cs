using OldCountry.Domain.Errors;

namespace OldCountry.Application.Tracks.StreamTrack;

public readonly union StreamTrackResult(LocalAudio, NotFoundError);
