using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using OldCountry.Api.Features.Playlists;
using OldCountry.Api.Features.Tracks;
using OldCountry.Api.Features.YouTubeMusic.DownloadTracks;
using OldCountry.Api.Features.YouTubeMusic.ImportPlaylist;
using OldCountry.Api.Features.YouTubeMusic.SearchYouTubeMusic;

namespace OldCountry.Api.Tests.Integration;

/// <summary>The YouTube Music endpoints against Postgres, with <see cref="FakeYtDlp"/> in place of yt-dlp.</summary>
public sealed class YouTubeMusicEndpointTests(ApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Importing_a_playlist_creates_youtube_only_tracks_and_a_library_playlist()
    {
        using var client = factory.CreateApiClient();

        var response = await client.PostAsJsonAsync("/api/youtube-music/imports", new { playlist = "PLfakeMix" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var report = (await response.Content.ReadFromJsonAsync<ImportPlaylistReport>(Json, Ct)).ShouldNotBeNull();
        report.Name.ShouldBe("Fake Mix");
        report.Entries.ShouldBe(3);   // Song, Other, Song again
        report.Skipped.ShouldBe(1);   // the entry without a duration

        var playlist = (await client.GetFromJsonAsync<PlaylistResponse>($"/api/playlists/{report.PlaylistId}", Json, Ct)).ShouldNotBeNull();
        playlist.Entries.Select(e => e.Title).ShouldBe(["Fake Song", "Other Song", "Fake Song"]);
        playlist.Entries.ShouldAllBe(e => !e.IsAvailableOffline);
        playlist.Entries[0].Artist.ShouldBe("Fake Artist");   // " - Topic" removed

        // Importing again reuses the tracks instead of duplicating them.
        var again = (await (await client.PostAsJsonAsync("/api/youtube-music/imports", new { playlist = "PLfakeMix" }, Ct))
            .Content.ReadFromJsonAsync<ImportPlaylistReport>(Json, Ct)).ShouldNotBeNull();
        again.TracksCreated.ShouldBe(0);
    }

    [Fact]
    public async Task Downloading_makes_a_track_playable_offline()
    {
        using var client = factory.CreateApiClient();

        var queued = await client.PostAsJsonAsync("/api/youtube-music/downloads", new { videoIds = new[] { FakeYtDlp.Song.Id.Value } }, Ct);
        queued.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var job = (await queued.Content.ReadFromJsonAsync<List<DownloadStatus>>(Json, Ct)).ShouldNotBeNull().ShouldHaveSingleItem();

        var done = await WaitFor(client, job.JobId);
        done.State.ShouldBe(DownloadState.Completed, done.Error);

        var track = (await client.GetFromJsonAsync<TrackResponse>($"/api/tracks/{done.TrackId}", Json, Ct)).ShouldNotBeNull();
        track.IsAvailableOffline.ShouldBeTrue();
        track.YouTubeVideoId.ShouldBe(FakeYtDlp.Song.Id.Value);

        var audio = await client.GetAsync($"/api/tracks/{done.TrackId}/audio", Ct);
        audio.StatusCode.ShouldBe(HttpStatusCode.OK);
        audio.Content.Headers.ContentType!.MediaType.ShouldBe("audio/wav");
    }

    [Fact]
    public async Task Search_marks_results_already_in_the_library()
    {
        using var client = factory.CreateApiClient();
        await client.PostAsJsonAsync("/api/tracks", new
        {
            title = "Other Song",
            artist = "Fake Artist",
            durationSeconds = 2,
            youTubeVideoId = FakeYtDlp.Other.Id.Value,
        }, Ct);

        var results = (await client.GetFromJsonAsync<List<YouTubeSearchResult>>("/api/youtube-music/search?query=fake", Json, Ct)).ShouldNotBeNull();

        results.Single(r => r.VideoId == FakeYtDlp.Other.Id.Value).TrackId.ShouldNotBeNull();
    }

    [Fact]
    public async Task Youtube_audio_plays_from_the_cache_with_ranges()
    {
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/youtube-music/videos/{FakeYtDlp.Other.Id.Value}/audio");
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 99);

        var response = await client.SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.PartialContent);
        (await response.Content.ReadAsByteArrayAsync(Ct)).Length.ShouldBe(100);
    }

    [Fact]
    public async Task Audio_outside_the_library_folders_is_never_served()
    {
        using var client = factory.CreateApiClient();
        var created = await client.PostAsJsonAsync("/api/tracks", new
        {
            title = "Sneaky",
            artist = "Nobody",
            durationSeconds = 1,
            localPath = "/etc/passwd",
            sizeBytes = 1,
        }, Ct);
        var track = (await created.Content.ReadFromJsonAsync<TrackResponse>(Json, Ct)).ShouldNotBeNull();

        (await client.GetAsync($"/api/tracks/{track.Id}/audio", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static async Task<DownloadStatus> WaitFor(HttpClient client, Guid jobId)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var jobs = await client.GetFromJsonAsync<List<DownloadStatus>>("/api/youtube-music/downloads", Json, Ct);
            if (jobs?.SingleOrDefault(j => j.JobId == jobId) is { State: DownloadState.Completed or DownloadState.Failed } done) return done;
            await Task.Delay(100, Ct);
        }
        throw new TimeoutException("The download did not finish within 10 seconds.");
    }
}
