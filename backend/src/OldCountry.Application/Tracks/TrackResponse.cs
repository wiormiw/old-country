using OldCountry.Domain.Downloads;
using OldCountry.Domain.Tracks;

namespace OldCountry.Application.Tracks;

/// <summary>A track in the library, as the apps show it.</summary>
/// <param name="Id">The id of the track.</param>
/// <param name="Title">The title of the track.</param>
/// <param name="Artist">The display name of the artist.</param>
/// <param name="Duration">The length of the recording.</param>
/// <param name="IsAvailableOffline">True when a local file exists, so the track can play without a connection.</param>
/// <param name="Codec">The codec of the local file, when there is one.</param>
/// <param name="YouTubeVideoId">The YouTube Music video id, when the track is linked.</param>
/// <param name="CreatedAt">When the track was added to the library.</param>
/// <param name="DownloadProblem">The reason that YouTube refused the last download. It stays until a download succeeds (PremiumOnly, Private, Unavailable, AgeRestricted, RegionBlocked, Failed).</param>
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
