using System.Collections.Concurrent;
using OldCountry.Application.Abstractions.YouTube;
using OldCountry.Application.Common.YouTube;
using OldCountry.Domain.YouTube;
using OldCountry.Tests.Shared;

namespace OldCountry.Api.Tests.Integration;

/// <summary>Replaces yt-dlp in the API tests: fixed search and playlist results, and real (silent) audio files.</summary>
internal sealed class FakeYtDlp : IYtDlp
{
    // Already parsed: the parser removes the YouTube " - Topic" channel suffix (see YtDlpJsonTests).
    public static readonly YtVideo Song = new(new YouTubeVideoId("fakeSong001"), "Fake Song", "Fake Artist", TimeSpan.FromSeconds(3), null);
    public static readonly YtVideo Other = new(new YouTubeVideoId("fakeSong002"), "Other Song", "Fake Artist", TimeSpan.FromSeconds(2), null);
    public static readonly YtVideo NoDuration = new(new YouTubeVideoId("fakeSong003"), "Live Stream", "Someone", null, null);

    public Task<IReadOnlyList<YtVideo>> SearchAsync(string query, int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<YtVideo>>([Song, Other]);

    public static readonly YtVideo Third = new(new YouTubeVideoId("fakeSong004"), "Third Song", "Fake Artist", TimeSpan.FromSeconds(2), null);

    /// <summary>What each playlist (by list id) holds "on YouTube" now. Tests change it between imports.</summary>
    public ConcurrentDictionary<string, YtPlaylist> Playlists { get; } = new();

    public Task<YtPlaylist> GetPlaylistAsync(Uri playlistUrl, CancellationToken cancellationToken)
    {
        var listId = System.Web.HttpUtility.ParseQueryString(playlistUrl.Query)["list"] ?? "";
        return Task.FromResult(Playlists.TryGetValue(listId, out var playlist)
            ? playlist
            : new YtPlaylist("Fake Mix", [Song, Other, Song, NoDuration]));
    }

    public Task<string> FetchAudioAsync(YouTubeVideoId videoId, string folder, CancellationToken cancellationToken) =>
        videoId == PremiumOnly.Id
            ? throw new YtDlpException(YouTubeRefusals.PremiumOnly, $"[youtube] {videoId}: This video is only available to Music Premium members")
            : Task.FromResult(TestAudio.WriteSilentWav(folder, $"{videoId.Value}.wav"));

    /// <summary>A video that only Music Premium members can play. Its download is refused.</summary>
    public static readonly YtVideo PremiumOnly = new(new YouTubeVideoId("premiumSong"), "Premium Song", "Fake Artist", TimeSpan.FromSeconds(2), null);

    /// <summary>Videos (by id) that answer "429 Too Many Requests" this number of more times before they download.</summary>
    public ConcurrentDictionary<string, int> RateLimitedTimes { get; } = new();

    public Task<YtDownload> DownloadAudioAsync(YouTubeVideoId videoId, string folder, CancellationToken cancellationToken)
    {
        if (videoId == PremiumOnly.Id)
            throw new YtDlpException(YouTubeRefusals.PremiumOnly, $"[youtube] {videoId}: This video is only available to Music Premium members");
        if (RateLimitedTimes.TryGetValue(videoId.Value, out var times) && times > 0)
        {
            RateLimitedTimes[videoId.Value] = times - 1;
            throw new YtDlpException(YouTubeRefusals.RateLimited, "unable to download video data: HTTP Error 429: Too Many Requests");
        }

        var video = Known(videoId);
        var path = TestAudio.WriteSilentWav(Path.Combine(folder, "Fake Artist"), $"{video.Title} [{videoId.Value}].wav", seconds: 3);
        return Task.FromResult(new YtDownload(path, video with { Artist = "Fake Artist" }, 128, 8_000));
    }

    /// <summary>The video with this id, from the fixed videos or from a playlist that a test set up.</summary>
    private YtVideo Known(YouTubeVideoId id) =>
        new[] { Song, Other, Third, PremiumOnly }.Concat(Playlists.Values.SelectMany(p => p.Entries)).FirstOrDefault(v => v.Id == id)
        ?? new YtVideo(id, $"Video {id}", "Fake Artist", TimeSpan.FromSeconds(2), null);
}
