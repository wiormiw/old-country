using OldCountry.Domain.Tracks;

namespace OldCountry.Application.Tracks;

/// <summary>A mapping that you write by hand, for the use cases of this feature. There is no AutoMapper.</summary>
internal static class TrackMapping
{
    public static TrackResponse ToResponse(this Track track, string artistName) => new(
        track.Id.Value,
        track.Title,
        artistName,
        track.Duration,
        track.IsAvailableOffline,
        track.LocalFile?.Quality.Codec,
        track.YouTubeVideoId?.Value,
        track.CreatedAt,
        track.DownloadProblem?.Reason);
}
