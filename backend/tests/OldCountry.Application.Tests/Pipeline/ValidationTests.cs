using OldCountry.Application.Common.Library;
using OldCountry.Application.Pipeline.Validation;
using OldCountry.Application.Playlists;
using OldCountry.Application.Playlists.AddPlaylistTracks;
using OldCountry.Application.Tracks.AddTrack;
using OldCountry.Application.Tracks.ListTracks;

namespace OldCountry.Application.Tests.Pipeline;

/// <summary>The validators give clear messages, with the JSON names that clients use as keys.</summary>
public sealed class ValidationTests
{
    private static AddTrackRequest ValidTrack => new()
    {
        Title = "Dust Road",
        Artist = "Old Country Band",
        DurationSeconds = 215,
        LocalPath = "/srv/music/dust-road.flac",
        SizeBytes = 1_000,
    };

    private static IReadOnlyDictionary<string, string[]> Errors<T>(FluentValidation.IValidator<T> validator, T request) =>
        validator.Validate(request).ToValidationError().Errors;

    [Fact]
    public void A_valid_track_passes() =>
        new AddTrackRequestValidator().Validate(ValidTrack).IsValid.ShouldBeTrue();

    [Fact]
    public void A_track_without_any_source_gets_the_custom_message_under_source()
    {
        var errors = Errors(new AddTrackRequestValidator(), ValidTrack with { LocalPath = null, SizeBytes = null });

        errors["source"].ShouldBe(["A track needs a local file path, a YouTube video id, or both."]);
    }

    [Fact]
    public void Keys_are_camel_case_like_the_json_body()
    {
        var errors = Errors(new AddTrackRequestValidator(), ValidTrack with { Title = "", DurationSeconds = 0, LocalPath = "relative/song.flac" });

        errors.Keys.ShouldBe(["title", "durationSeconds", "localPath"], ignoreOrder: true);
        errors["title"].ShouldBe(["Give the track a title."]);
        errors["localPath"].ShouldBe(["The local path must be absolute, e.g. /srv/music/song.flac."]);
    }

    [Fact]
    public void A_local_path_needs_its_size() =>
        Errors(new AddTrackRequestValidator(), ValidTrack with { SizeBytes = null }).ShouldContainKey("sizeBytes");

    [Theory]
    [InlineData("short")]
    [InlineData("has spaces!")]
    public void A_bad_youtube_id_explains_where_to_find_one(string id) =>
        Errors(new AddTrackRequestValidator(), ValidTrack with { YouTubeVideoId = id })["youTubeVideoId"].Single().ShouldContain("after \"v=\"");

    [Fact]
    public void Collection_items_are_keyed_by_index()
    {
        var errors = Errors(new AddPlaylistTracksRequestValidator(), new AddPlaylistTracksRequest { TrackIds = [Guid.CreateVersion7(), Guid.Empty] });

        errors.ShouldContainKey("trackIds[1]");
    }

    [Fact]
    public void An_empty_track_list_is_refused() =>
        Errors(new AddPlaylistTracksRequestValidator(), new AddPlaylistTracksRequest { TrackIds = [] })["trackIds"]
            .ShouldBe(["Pick at least one track to add."]);

    [Fact]
    public void A_blank_playlist_name_is_refused() =>
        Errors(new PlaylistDetailsRequestValidator(), new PlaylistDetailsRequest { Name = " " }).ShouldContainKey("name");

    [Theory]
    [InlineData(0, 50, "page")]
    [InlineData(1, 0, "pageSize")]
    [InlineData(1, 201, "pageSize")]
    public void Paging_bounds_are_checked(int page, int pageSize, string key) =>
        Errors(new ListTracksRequestValidator(), new ListTracksRequest(page, pageSize)).ShouldContainKey(key);

    [Fact]
    public void Library_options_refuse_relative_roots_and_long_intervals()
    {
        var result = new LibraryOptionsValidator().Validate(new LibraryOptions { Roots = ["music"], RescanInterval = TimeSpan.FromDays(8) });

        result.Errors.Select(e => e.ErrorMessage).ShouldBe(
        [
            "Library folder 'music' must be an absolute path.",
            "RescanInterval must be between 00:00:00 (off) and 7.00:00:00 (a week).",
        ], ignoreOrder: true);
    }
}
