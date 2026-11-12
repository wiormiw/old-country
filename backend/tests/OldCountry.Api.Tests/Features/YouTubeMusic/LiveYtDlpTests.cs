using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OldCountry.Api.Features.YouTubeMusic;
using OldCountry.Api.Tests.Features.Tracks;
using OldCountry.Domain.Library;

namespace OldCountry.Api.Tests.Features.YouTubeMusic;

/// <summary>
/// Against real YouTube with the installed yt-dlp. Off by default (network, rate limits, slow);
/// run with <c>OLDCOUNTRY_LIVE_YTDLP=1 dotnet test</c> after upgrading yt-dlp or when YouTube changes.
/// </summary>
public sealed class LiveYtDlpTests : IDisposable
{
    private static readonly YouTubeVideoId Video = new("dQw4w9WgXcQ");
    private readonly string _directory = TestAudio.NewTempDirectory();

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static YtDlpProcess YtDlp()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("OLDCOUNTRY_LIVE_YTDLP") == "1", "Set OLDCOUNTRY_LIVE_YTDLP=1 to run live yt-dlp tests.");
        return new YtDlpProcess(Options.Create(new YouTubeMusicOptions()), NullLogger<YtDlpProcess>.Instance);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Search_returns_songs_with_artist_and_duration()
    {
        var results = await YtDlp().SearchAsync("rick astley never gonna give you up", 3, Ct);

        results.ShouldNotBeEmpty();
        results.ShouldAllBe(r => r.Artist != null && r.Duration != null);
    }

    [Fact]
    public async Task Fetching_saves_the_native_audio_stream()
    {
        var path = await YtDlp().FetchAudioAsync(Video, _directory, Ct);

        Path.GetFileName(path).ShouldStartWith(Video.Value);
        new FileInfo(path).Length.ShouldBeGreaterThan(100_000);
    }

    [Fact]
    public async Task Downloading_converts_tags_and_reports_the_final_file()
    {
        var download = await YtDlp().DownloadAudioAsync(Video, _directory, Ct);

        File.Exists(download.Path).ShouldBeTrue();
        download.Path.ShouldEndWith($"[{Video.Value}].opus");
        download.Video.Id.ShouldBe(Video);
        download.Video.Duration.ShouldNotBeNull();
        YtDlpJson.CodecOf(download.Path).ShouldBe(AudioCodec.Opus);
    }
}
