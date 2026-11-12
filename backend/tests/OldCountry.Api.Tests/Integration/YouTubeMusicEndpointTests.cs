using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using OldCountry.Api.Features.Playlists;
using OldCountry.Api.Features.YouTubeMusic;
using OldCountry.Api.Features.Tracks;
using OldCountry.Api.Features.Tracks.GetTrack;
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
        playlist.Entries[0].Artist.ShouldBe("Fake Artist");

        // Importing again syncs the same playlist: nothing new on YouTube, so nothing changes.
        var againResponse = await client.PostAsJsonAsync("/api/youtube-music/imports", new { playlist = "PLfakeMix" }, Ct);
        againResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var again = (await againResponse.Content.ReadFromJsonAsync<ImportPlaylistReport>(Json, Ct)).ShouldNotBeNull();
        again.PlaylistId.ShouldBe(report.PlaylistId);
        again.IsNew.ShouldBeFalse();
        again.Added.ShouldBe(0);
        again.Entries.ShouldBe(3);
        again.TracksCreated.ShouldBe(0);
    }

    [Fact]
    public async Task Importing_again_adds_songs_new_on_youtube_and_keeps_the_library_playlists_edits()
    {
        using var client = factory.CreateApiClient();
        var youTube = (FakeYtDlp)factory.Services.GetRequiredService<IYtDlp>();
        youTube.Playlists["PLsyncMix"] = new YtPlaylist("Sync Mix", [FakeYtDlp.Song, FakeYtDlp.Other]);
        var first = await Import(client, "PLsyncMix");
        first.IsNew.ShouldBeTrue();

        // Edit it here: rename, and remove the first song (which is still on YouTube).
        (await client.PutAsJsonAsync($"/api/playlists/{first.PlaylistId}", new { name = "My mix", description = "edited" }, Ct))
            .EnsureSuccessStatusCode();
        (await client.DeleteAsync($"/api/playlists/{first.PlaylistId}/tracks/0", Ct)).EnsureSuccessStatusCode();

        // Meanwhile a song is added on YouTube.
        youTube.Playlists["PLsyncMix"] = new YtPlaylist("Sync Mix", [FakeYtDlp.Song, FakeYtDlp.Other, FakeYtDlp.Third]);
        var sync = await Import(client, "PLsyncMix");

        sync.PlaylistId.ShouldBe(first.PlaylistId);
        sync.IsNew.ShouldBeFalse();
        sync.Added.ShouldBe(1);
        var playlist = (await client.GetFromJsonAsync<PlaylistResponse>($"/api/playlists/{first.PlaylistId}", Json, Ct)).ShouldNotBeNull();
        playlist.Name.ShouldBe("My mix");
        playlist.Description.ShouldBe("edited");
        playlist.Entries.Select(e => e.Title).ShouldBe(["Other Song", "Third Song"], "the removed song stays removed");

        (await Import(client, "PLsyncMix")).Added.ShouldBe(0, "nothing new since the last sync");
    }

    [Fact]
    public async Task An_earlier_import_that_was_renamed_is_found_by_its_songs_and_synced()
    {
        using var client = factory.CreateApiClient();
        var youTube = (FakeYtDlp)factory.Services.GetRequiredService<IYtDlp>();
        YtVideo a = new(new("adoptSong01"), "Adopt A", "Band", TimeSpan.FromSeconds(2), null),
            b = new(new("adoptSong02"), "Adopt B", "Band", TimeSpan.FromSeconds(2), null),
            c = new(new("adoptSong03"), "Adopt C", "Band", TimeSpan.FromSeconds(2), null);

        // A playlist like the ones imported before linking: YouTube songs, no link, its own name.
        youTube.Playlists["PLadoptSeed"] = new YtPlaylist("Seed", [a, b]);
        var seed = await Import(client, "PLadoptSeed");
        var trackIds = (await client.GetFromJsonAsync<PlaylistResponse>($"/api/playlists/{seed.PlaylistId}", Json, Ct))
            .ShouldNotBeNull().Entries.Select(e => e.TrackId).ToArray();
        var created = await client.PostAsJsonAsync("/api/playlists", new { name = "Renamed long ago", description = "my words" }, Ct);
        var earlier = (await created.Content.ReadFromJsonAsync<PlaylistSummaryResponse>(Json, Ct)).ShouldNotBeNull();
        (await client.PostAsJsonAsync($"/api/playlists/{earlier.Id}/tracks", new { trackIds }, Ct)).EnsureSuccessStatusCode();

        youTube.Playlists["PLadopt"] = new YtPlaylist("Adopt Mix", [a, b, c]);
        var sync = await Import(client, "PLadopt");

        sync.IsNew.ShouldBeFalse();
        sync.PlaylistId.ShouldBe(earlier.Id);
        sync.Added.ShouldBe(1);
        sync.Name.ShouldBe("Renamed long ago");
        (await Import(client, "PLadopt")).PlaylistId.ShouldBe(earlier.Id, "linked from now on");
    }

    [Fact]
    public async Task A_url_and_the_bare_list_id_are_the_same_playlist()
    {
        using var client = factory.CreateApiClient();
        var first = await Import(client, "PLsameList");
        var again = await Import(client, "https://www.youtube.com/playlist?list=PLsameList&si=shared");
        again.PlaylistId.ShouldBe(first.PlaylistId);
        again.IsNew.ShouldBeFalse();
    }

    private static async Task<ImportPlaylistReport> Import(HttpClient client, string playlist)
    {
        var response = await client.PostAsJsonAsync("/api/youtube-music/imports", new { playlist }, Ct);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<ImportPlaylistReport>(Json, Ct)).ShouldNotBeNull();
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

        var track = (await client.GetFromJsonAsync<TrackDetailResponse>($"/api/tracks/{done.TrackId}", Json, Ct)).ShouldNotBeNull().Track;
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
