using System.Text.Json;
using OldCountry.Api.Features.YouTubeMusic;
using OldCountry.Domain.Library;

namespace OldCountry.Api.Tests.Features.YouTubeMusic;

/// <summary>Parsing yt-dlp's JSON, using output captured from yt-dlp 2026.10.26.</summary>
public sealed class YtDlpJsonTests
{
    // `yt-dlp --flat-playlist -J "ytsearch1:..."`, trimmed to the fields that matter.
    private const string RealSearch = """
        {"title": "rick astley never gonna give you up official audio", "entries": [{"id": "3BFTio5296w",
        "title": "Never Gonna Give You Up (2022 Remaster)", "uploader": "Rick Astley", "channel": "Rick Astley",
        "duration": 214, "thumbnails": [{"url": "https://i.ytimg.com/vi/3BFTio5296w/small.jpg", "height": 202},
        {"url": "https://i.ytimg.com/vi/3BFTio5296w/large.jpg", "height": 404}]},
        {"id": "not-a-video-id-at-all", "title": "A channel, not a video"}]}
        """;

    [Fact]
    public void A_flat_search_yields_videos_with_artist_duration_and_the_largest_thumbnail()
    {
        var result = YtDlpJson.ParsePlaylist(RealSearch);

        var video = result.Entries.ShouldHaveSingleItem();
        video.Id.ShouldBe(new YouTubeVideoId("3BFTio5296w"));
        video.Title.ShouldBe("Never Gonna Give You Up (2022 Remaster)");
        video.Artist.ShouldBe("Rick Astley");
        video.Duration.ShouldBe(TimeSpan.FromSeconds(214));
        video.ThumbnailUrl.ShouldBe("https://i.ytimg.com/vi/3BFTio5296w/large.jpg");
    }

    [Fact]
    public void Entries_that_are_not_videos_are_dropped() =>
        YtDlpJson.ParsePlaylist(RealSearch).Entries.ShouldAllBe(v => v.Id.Value.Length == YouTubeVideoId.Length);

    [Fact]
    public void A_download_line_gives_the_final_path_and_stream_properties()
    {
        const string output = """
            [ExtractAudio] Destination: ignored chatter
            {"id": "dQw4w9WgXcQ", "title": "Never Gonna Give You Up", "artists": ["Rick Astley"], "duration": 213.06, "abr": 129.8, "asr": 48000, "filepath": "/srv/music/YouTube Music/Rick Astley/Never Gonna Give You Up [dQw4w9WgXcQ].opus"}
            """;

        var download = YtDlpJson.ParseDownload(output);

        download.Path.ShouldEndWith("[dQw4w9WgXcQ].opus");
        download.Video.Artist.ShouldBe("Rick Astley");
        download.Video.Duration.ShouldBe(TimeSpan.FromSeconds(213));
        download.BitrateKbps.ShouldBe(130);
        download.SampleRateHz.ShouldBe(48_000);
        YtDlpJson.CodecOf(download.Path).ShouldBe(AudioCodec.Opus);
    }

    [Fact]
    public void A_download_falls_back_to_requested_downloads_for_the_path()
    {
        const string output = """{"id": "dQw4w9WgXcQ", "title": "Song", "requested_downloads": [{"filepath": "/srv/music/a [dQw4w9WgXcQ].m4a"}]}""";

        YtDlpJson.ParseDownload(output).Path.ShouldBe("/srv/music/a [dQw4w9WgXcQ].m4a");
    }

    [Fact]
    public void Output_that_is_not_json_is_a_bad_output_error() =>
        Should.Throw<YtDlpException>(() => YtDlpJson.ParsePlaylist("<html>rate limited</html>")).Code.ShouldBe("youtube.bad_output");

    [Theory]
    [InlineData("Rick Astley - Topic", "Rick Astley")]
    [InlineData("Rick Astley", "Rick Astley")]
    [InlineData(null, null)]
    public void Topic_channels_lose_their_suffix(string? channel, string? artist) =>
        YtDlpJson.CleanChannel(channel).ShouldBe(artist);

    [Fact]
    public void Music_fields_win_over_the_channel()
    {
        using var document = JsonDocument.Parse("""{"artist": "Real Artist", "uploader": "Some Channel - Topic"}""");

        YtDlpJson.ArtistOf(document.RootElement).ShouldBe("Real Artist");
    }

    [Fact]
    public void Errors_are_summarised_to_the_last_ERROR_line()
    {
        const string stderr = """
            [youtube] dQw4w9WgXcQ: Downloading webpage
            WARNING: something harmless
            ERROR: [youtube] dQw4w9WgXcQ: Video unavailable. This video is private
            """;

        YtDlpJson.SummarizeError(stderr).ShouldBe("[youtube] dQw4w9WgXcQ: Video unavailable. This video is private");
    }
}
