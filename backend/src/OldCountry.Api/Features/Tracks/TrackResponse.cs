using OldCountry.Domain.Library;

namespace OldCountry.Api.Features.Tracks;

/// <summary>A track in the library, as the desktop app sees it.</summary>
/// <param name="Id">The track's id.</param>
/// <param name="Title">Track title.</param>
/// <param name="Artist">Display name of the artist.</param>
/// <param name="Duration">Length of the recording.</param>
/// <param name="IsAvailableOffline">Whether a local file exists and the track can play without a connection.</param>
/// <param name="Codec">Codec of the local file, when there is one.</param>
/// <param name="YouTubeVideoId">The YouTube Music video id, when linked.</param>
/// <param name="CreatedAt">When the track was added to the library.</param>
/// <param name="DownloadProblem">Why YouTube refused the last download, until one succeeds (PremiumOnly, Private, Unavailable, AgeRestricted, RegionBlocked, Failed).</param>
public sealed record TrackResponse(
    Guid Id,
    string Title,
    string Artist,
    TimeSpan Duration,
    bool IsAvailableOffline,
    AudioCodec? Codec,
    string? YouTubeVideoId,
    DateTimeOffset CreatedAt,
    DownloadRefusal? DownloadProblem);

/// <summary>Hand-written mapping, shared by this feature's use cases. No AutoMapper.</summary>
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
