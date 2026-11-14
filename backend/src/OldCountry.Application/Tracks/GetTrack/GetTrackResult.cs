using OldCountry.Domain.Errors;

namespace OldCountry.Application.Tracks.GetTrack;

/// <summary>All the possible results of this use case. The switch of the endpoint must handle each case.</summary>
public readonly union GetTrackResult(TrackDetailResponse, NotFoundError);
