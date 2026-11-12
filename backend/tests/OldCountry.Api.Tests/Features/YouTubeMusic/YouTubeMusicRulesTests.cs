using Microsoft.Extensions.Options;
using OldCountry.Api.Common.Library;
using OldCountry.Api.Common.Validation;
using OldCountry.Api.Features.YouTubeMusic;
using OldCountry.Api.Features.YouTubeMusic.DownloadTracks;
using OldCountry.Api.Features.YouTubeMusic.ImportPlaylist;
using OldCountry.Api.Features.YouTubeMusic.SearchYouTubeMusic;
using OldCountry.Api.Tests.Features.Tracks;
using OldCountry.Domain.Errors;
using OldCountry.Domain.Library;

namespace OldCountry.Api.Tests.Features.YouTubeMusic;

/// <summary>Input rules and small in-memory pieces of the YouTube Music feature.</summary>
public sealed class YouTubeMusicRulesTests : IDisposable
{
    private readonly string _library = TestAudio.NewTempDirectory();

    public void Dispose() => Directory.Delete(_library, recursive: true);

    [Theory]
    [InlineData("liked", "https://music.youtube.com/playlist?list=LM")]
    [InlineData("  LIKED ", "https://music.youtube.com/playlist?list=LM")]
    [InlineData("PLabc_DEF-123", "https://music.youtube.com/playlist?list=PLabc_DEF-123")]
    [InlineData("https://music.youtube.com/playlist?list=OLAK5uy_x1", "https://music.youtube.com/playlist?list=OLAK5uy_x1")]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ&list=PL123", "https://music.youtube.com/playlist?list=PL123")]
    public void Playlist_inputs_normalise_to_one_music_youtube_url(string input, string expected) =>
        PlaylistSource.ToUrl(input).ShouldBe(new Uri(expected));

    [Theory]
    [InlineData("https://evil.example/playlist?list=PL123")]
    [InlineData("http://music.youtube.com/playlist?list=PL123")]
    [InlineData("https://music.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("not a playlist!")]
    public void Anything_else_is_not_a_playlist(string input) =>
        PlaylistSource.ToUrl(input).ShouldBeNull();

    [Fact]
    public void Importing_liked_songs_without_cookies_explains_what_is_needed()
    {
        var validator = new ImportPlaylistRequestValidator(Options.Create(new YouTubeMusicOptions()));

        var errors = validator.Validate(new ImportPlaylistRequest { Playlist = "liked" }).ToValidationError().Errors;

        errors["playlist"].Single().ShouldContain("YouTubeMusic:CookiesFile");
    }

    [Fact]
    public void Download_requests_name_each_bad_id()
    {
        var errors = new DownloadTracksRequestValidator()
            .Validate(new DownloadTracksRequest { VideoIds = ["dQw4w9WgXcQ", "nope"] }).ToValidationError().Errors;

        errors.Keys.ShouldBe(["videoIds[1]"]);
        errors["videoIds[1]"].Single().ShouldContain("'nope'");
    }

    [Fact]
    public void A_search_needs_a_query() =>
        new SearchYouTubeMusicRequestValidator().Validate(new SearchYouTubeMusicRequest(" ")).ToValidationError().Errors.ShouldContainKey("query");

    [Fact]
    public void Downloads_default_to_a_folder_inside_the_first_library_root() =>
        new YouTubeMusicOptions().ResolveDownloadFolder(new LibraryOptions { Roots = [_library] }).Value
            .ShouldBe(Path.Combine(_library, YouTubeMusicOptions.DefaultFolderName));

    [Fact]
    public void A_download_folder_outside_the_library_is_refused() =>
        new YouTubeMusicOptions { DownloadFolder = "/tmp/elsewhere" }.ResolveDownloadFolder(new LibraryOptions { Roots = [_library] }).Value
            .ShouldBeOfType<ValidationError>();

    [Fact]
    public void Without_a_library_there_is_nowhere_to_download() =>
        new YouTubeMusicOptions().ResolveDownloadFolder(new LibraryOptions()).Value.ShouldBeOfType<ValidationError>();

    [Theory]
    [InlineData("[youtube] SxXfA-g9SQc: This video is only available to Music Premium members", YouTubeRefusals.PremiumOnly)]
    [InlineData("[youtube] abc: Private video. Sign in if you've been granted access to this video", YouTubeRefusals.Private)]
    [InlineData("[youtube] abc: Video unavailable. This video has been removed by the uploader", YouTubeRefusals.Unavailable)]
    [InlineData("[youtube] abc: Sign in to confirm your age. This video may be inappropriate for some users.", YouTubeRefusals.AgeRestricted)]
    [InlineData("[youtube] abc: The uploader has not made this video available in your country", YouTubeRefusals.RegionBlocked)]
    [InlineData("[youtube] abc: Video unavailable. This video is not available in your country", YouTubeRefusals.RegionBlocked)]
    [InlineData("[youtube] abc: Sign in to confirm you’re not a bot", YouTubeRefusals.RateLimited)]
    [InlineData("unable to download video data: HTTP Error 429: Too Many Requests", YouTubeRefusals.RateLimited)]
    [InlineData("unable to download video data: HTTP Error 403: Forbidden", YouTubeRefusals.Failed)]
    public void Youtube_refusals_are_classified_by_what_yt_dlp_says(string reason, string code) =>
        YouTubeRefusals.Classify(reason).ShouldBe(code);

    [Fact]
    public void Only_refusals_about_the_video_are_remembered_on_the_track()
    {
        YouTubeRefusals.RefusalOf(YouTubeRefusals.PremiumOnly).ShouldBe(DownloadRefusal.PremiumOnly);
        YouTubeRefusals.RefusalOf(YouTubeRefusals.Failed).ShouldBe(DownloadRefusal.Failed);
        YouTubeRefusals.RefusalOf(YouTubeRefusals.RateLimited).ShouldBeNull();
        YouTubeRefusals.RefusalOf("youtube.ytdlp_missing").ShouldBeNull();
        YouTubeRefusals.RefusalOf("youtube.timeout").ShouldBeNull();
    }

    [Fact]
    public void Rate_limits_in_a_row_double_the_rest_up_to_eight_times_and_a_success_resets_it()
    {
        var pauses = new DownloadPauses();
        var now = new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
        var rest = TimeSpan.FromMinutes(15);

        TimeSpan[] waits = [.. Enumerable.Range(0, 5).Select(_ => pauses.RateLimited(now, rest, "429") - now)];

        waits.ShouldBe([rest, rest * 2, rest * 4, rest * 8, rest * 8]);
        pauses.Remaining(now).ShouldBe(rest * 8);
        pauses.Succeeded();
        pauses.Remaining(now).ShouldBeNull();
        (pauses.RateLimited(now, rest, "429") - now).ShouldBe(rest);
    }

    [Fact]
    public async Task The_doorbell_wakes_the_worker_at_once_and_otherwise_times_out()
    {
        var doorbell = new DownloadDoorbell();
        doorbell.Ring();
        doorbell.Ring();   // rings while nobody listens fold into one

        var rung = System.Diagnostics.Stopwatch.StartNew();
        await doorbell.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        rung.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(1));

        var quiet = System.Diagnostics.Stopwatch.StartNew();
        await doorbell.WaitAsync(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);
        quiet.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(150));
    }

    [Fact]
    public void Paths_escaping_the_library_with_dot_dot_are_outside_it() =>
        LibraryPaths.IsInsideLibrary(Path.Combine(_library, "..", "etc", "passwd"), [_library]).ShouldBeFalse();

    [Fact]
    public void A_symlink_inside_the_library_pointing_outside_is_outside_it()
    {
        var outside = Path.Combine(Path.GetTempPath(), $"old-country-outside-{Guid.NewGuid():N}.flac");
        File.WriteAllText(outside, "secret");
        try
        {
            var link = Path.Combine(_library, "innocent.flac");
            File.CreateSymbolicLink(link, outside);

            LibraryPaths.IsInsideLibrary(link, [_library]).ShouldBeFalse();
        }
        finally
        {
            File.Delete(outside);
        }
    }
}
