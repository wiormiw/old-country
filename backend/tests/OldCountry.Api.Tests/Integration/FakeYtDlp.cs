using OldCountry.Api.Features.YouTubeMusic;
using OldCountry.Api.Tests.Features.Tracks;
using OldCountry.Domain.Library;

namespace OldCountry.Api.Tests.Integration;

/// <summary>Stands in for yt-dlp in API tests: canned search and playlist results, and real (silent) audio files.</summary>
internal sealed class FakeYtDlp : IYtDlp
{
    public static readonly YtVideo Song = new(new YouTubeVideoId("fakeSong001"), "Fake Song", "Fake Artist - Topic", TimeSpan.FromSeconds(3), null);
    public static readonly YtVideo Other = new(new YouTubeVideoId("fakeSong002"), "Other Song", "Fake Artist", TimeSpan.FromSeconds(2), null);
    public static readonly YtVideo NoDuration = new(new YouTubeVideoId("fakeSong003"), "Live Stream", "Someone", null, null);

    public Task<IReadOnlyList<YtVideo>> SearchAsync(string query, int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<YtVideo>>([Song, Other]);

    public Task<YtPlaylist> GetPlaylistAsync(Uri playlistUrl, CancellationToken cancellationToken) =>
        Task.FromResult(new YtPlaylist("Fake Mix", [Song, Other, Song, NoDuration]));

    public Task<string> FetchAudioAsync(YouTubeVideoId videoId, string folder, CancellationToken cancellationToken) =>
        Task.FromResult(TestAudio.WriteSilentWav(folder, $"{videoId.Value}.wav"));

    public Task<YtDownload> DownloadAudioAsync(YouTubeVideoId videoId, string folder, CancellationToken cancellationToken)
    {
        var video = videoId == Song.Id ? Song : Other;
        var path = TestAudio.WriteSilentWav(Path.Combine(folder, "Fake Artist"), $"{video.Title} [{videoId.Value}].wav", seconds: 3);
        return Task.FromResult(new YtDownload(path, video with { Artist = "Fake Artist" }, 128, 8_000));
    }
}
