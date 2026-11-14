using OldCountry.Domain.Artists;
using OldCountry.Domain.Errors;
using OldCountry.Domain.Tracks;
using OldCountry.Domain.YouTube;

namespace OldCountry.Domain.Tests.Tracks;

public sealed class TrackTests
{
    private static readonly ArtistId Artist = ArtistId.New();
    private static readonly LocalFile File = new("/srv/music/song.flac", 1_000, new AudioQuality(AudioCodec.Flac, 1_000, 44_100));
    private static readonly YouTubeVideoId Video = new("dQw4w9WgXcQ");

    [Fact]
    public void A_track_needs_a_local_file_or_a_youtube_video() =>
        Track.Create("Song", Artist, TimeSpan.FromMinutes(3), localFile: null, youTubeVideoId: null).Value
            .ShouldBeOfType<ValidationError>().Errors.ShouldContainKey("source");

    [Fact]
    public void A_local_track_is_available_offline()
    {
        var track = Track.Create("Song", Artist, TimeSpan.FromMinutes(3), File, null).Value.ShouldBeOfType<Track>();

        track.IsAvailableOffline.ShouldBeTrue();
        track.YouTubeVideoId.ShouldBeNull();
    }

    [Fact]
    public void A_youtube_track_is_not_available_offline_until_downloaded()
    {
        var track = Track.Create("Song", Artist, TimeSpan.FromMinutes(3), null, Video).Value.ShouldBeOfType<Track>();
        track.IsAvailableOffline.ShouldBeFalse();

        track.AttachLocalFile(File);

        track.IsAvailableOffline.ShouldBeTrue();
        track.YouTubeVideoId.ShouldBe(Video);
    }

    [Fact]
    public void The_title_is_trimmed() =>
        Track.Create("  Song  ", Artist, TimeSpan.FromMinutes(3), File, null).Value.ShouldBeOfType<Track>().Title.ShouldBe("Song");

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_title_is_refused(string title) =>
        Track.Create(title, Artist, TimeSpan.FromMinutes(3), File, null).Value
            .ShouldBeOfType<ValidationError>().Errors.ShouldContainKey("title");

    [Fact]
    public void A_zero_duration_is_refused() =>
        Track.Create("Song", Artist, TimeSpan.Zero, File, null).Value
            .ShouldBeOfType<ValidationError>().Errors.ShouldContainKey("duration");

    [Fact]
    public void Ids_are_time_ordered() =>
        TrackId.New().Value.Version.ShouldBe(7);

    [Fact]
    public void Union_results_switch_exhaustively_over_their_cases()
    {
        var outcome = Track.Create("Song", Artist, TimeSpan.FromMinutes(3), File, null) switch
        {
            Track track => track.Title,
            ValidationError error => error.Code,
        };

        outcome.ShouldBe("Song");
    }
}
