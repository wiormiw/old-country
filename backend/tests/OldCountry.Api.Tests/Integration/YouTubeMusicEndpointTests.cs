using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Application.Abstractions.YouTube;
using OldCountry.Application.Downloads;
using OldCountry.Application.Downloads.ListDownloads;
using OldCountry.Application.Downloads.QueuePlaylistDownloads;
using OldCountry.Application.Playlists;
using OldCountry.Application.Tracks;
using OldCountry.Application.Tracks.GetTrack;
using OldCountry.Application.YouTubeMusic.ImportPlaylist;
using OldCountry.Application.YouTubeMusic.SearchYouTubeMusic;
using OldCountry.Application.YouTubeMusic.SyncDuePlaylists;
using OldCountry.Domain.Downloads;
using OldCountry.Domain.YouTube;
using OldCountry.Infrastructure.Persistence;

namespace OldCountry.Api.Tests.Integration;

/// <summary>The YouTube Music endpoints with Postgres, and <see cref="FakeYtDlp"/> in place of yt-dlp.</summary>
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
        report.Entries.ShouldBe(3);   // Song, Other, and Song again.
        report.Skipped.ShouldBe(1);   // The entry without a duration.

        var playlist = (await client.GetFromJsonAsync<PlaylistResponse>($"/api/playlists/{report.PlaylistId}", Json, Ct)).ShouldNotBeNull();
        playlist.Entries.Select(e => e.Title).ShouldBe(["Fake Song", "Other Song", "Fake Song"]);
        playlist.Entries.ShouldAllBe(e => !e.IsAvailableOffline);
        playlist.Entries[0].Artist.ShouldBe("Fake Artist");

        // A second import syncs the same playlist. Nothing is new on YouTube, so nothing changes.
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

        // Change it here: a new name, and remove the first track (it is still on YouTube).
        (await client.PutAsJsonAsync($"/api/playlists/{first.PlaylistId}", new { name = "My mix", description = "edited" }, Ct))
            .EnsureSuccessStatusCode();
        (await client.DeleteAsync($"/api/playlists/{first.PlaylistId}/tracks/0", Ct)).EnsureSuccessStatusCode();

        // At the same time, a track is added on YouTube.
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

        // A playlist like the ones imported before the link existed: YouTube tracks, no link, its own name.
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
    public async Task The_sync_button_adds_songs_new_on_youtube_and_records_when()
    {
        using var client = factory.CreateApiClient();
        var youTube = (FakeYtDlp)factory.Services.GetRequiredService<IYtDlp>();
        YtVideo a = new(new("syncNowSng1"), "Sync A", "Band", TimeSpan.FromSeconds(2), null),
            b = new(new("syncNowSng2"), "Sync B", "Band", TimeSpan.FromSeconds(2), null);
        youTube.Playlists["PLsyncNow"] = new YtPlaylist("Sync Now", [a]);
        var imported = await Import(client, "PLsyncNow");

        youTube.Playlists["PLsyncNow"] = new YtPlaylist("Sync Now", [a, b]);
        var response = await client.PostAsync($"/api/youtube-music/playlists/{imported.PlaylistId}/sync", null, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<ImportPlaylistReport>(Json, Ct))!.Added.ShouldBe(1);
        var playlist = (await client.GetFromJsonAsync<PlaylistResponse>($"/api/playlists/{imported.PlaylistId}", Json, Ct)).ShouldNotBeNull();
        playlist.YouTubeListId.ShouldBe("PLsyncNow");
        playlist.YouTubeSyncedAt.ShouldNotBeNull();
        playlist.Entries.Select(e => e.Title).ShouldBe(["Sync A", "Sync B"]);
    }

    [Fact]
    public async Task Syncing_a_playlist_that_is_not_from_youtube_or_does_not_exist_says_why()
    {
        using var client = factory.CreateApiClient();
        var created = await client.PostAsJsonAsync("/api/playlists", new { name = "Made here" }, Ct);
        var mine = (await created.Content.ReadFromJsonAsync<PlaylistSummaryResponse>(Json, Ct)).ShouldNotBeNull();
        mine.YouTubeListId.ShouldBeNull();

        (await client.PostAsync($"/api/youtube-music/playlists/{mine.Id}/sync", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.PostAsync($"/api/youtube-music/playlists/{Guid.CreateVersion7()}/sync", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_scheduler_syncs_playlists_whose_last_sync_is_old_and_leaves_recent_ones()
    {
        using var client = factory.CreateApiClient();
        var youTube = (FakeYtDlp)factory.Services.GetRequiredService<IYtDlp>();
        YtVideo a = new(new("autoSong001"), "Auto A", "Band", TimeSpan.FromSeconds(2), null),
            b = new(new("autoSong002"), "Auto B", "Band", TimeSpan.FromSeconds(2), null),
            c = new(new("autoSong003"), "Auto C", "Band", TimeSpan.FromSeconds(2), null);
        youTube.Playlists["PLautoSync"] = new YtPlaylist("Auto Sync", [a]);
        var imported = await Import(client, "PLautoSync");

        // A track is added on YouTube. A round that treats each sync as old finds it.
        youTube.Playlists["PLautoSync"] = new YtPlaylist("Auto Sync", [a, b]);
        (await SyncDue(TimeSpan.Zero)).ShouldBeGreaterThan(0);
        (await Titles(client, imported.PlaylistId)).ShouldBe(["Auto A", "Auto B"]);

        // The playlist synced now, so a round with a one-hour interval does not touch it.
        youTube.Playlists["PLautoSync"] = new YtPlaylist("Auto Sync", [a, b, c]);
        await SyncDue(TimeSpan.FromHours(1));
        (await Titles(client, imported.PlaylistId)).ShouldBe(["Auto A", "Auto B"]);
    }

    /// <summary>One round of the automatic sync, as the scheduler loop runs it.</summary>
    private async Task<int> SyncDue(TimeSpan olderThan)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<SyncDuePlaylistsCommand, int>>();
        return await handler.Handle(new SyncDuePlaylistsCommand(olderThan), Ct);
    }

    private static async Task<string[]> Titles(HttpClient client, Guid playlistId) =>
        [.. (await client.GetFromJsonAsync<PlaylistResponse>($"/api/playlists/{playlistId}", Json, Ct))!.Entries.Select(e => e.Title)];

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
    public async Task A_video_that_youtube_refuses_is_a_403_with_its_reason_not_a_server_error()
    {
        using var client = factory.CreateApiClient();

        var response = await client.GetAsync($"/api/youtube-music/videos/{FakeYtDlp.PremiumOnly.Id.Value}/audio", Ct);

        // A 4xx status: the 5xx metrics and alerts count only the failures of the server.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe("youtube.premium_only");
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

    [Fact]
    public async Task Downloading_a_playlist_gets_what_is_missing_and_remembers_what_youtube_refused()
    {
        using var client = factory.CreateApiClient();
        var youTube = (FakeYtDlp)factory.Services.GetRequiredService<IYtDlp>();
        YtVideo free = new(new("dlFreeSong1"), "Free Song", "Band", TimeSpan.FromSeconds(2), null);
        youTube.Playlists["PLdownloadAll"] = new YtPlaylist("Download All", [free, FakeYtDlp.PremiumOnly]);
        var imported = await Import(client, "PLdownloadAll");

        var first = await DownloadPlaylist(client, imported.PlaylistId);
        first.Queued.ShouldBe(2);
        var jobs = await WaitForVideos(client, [free.Id.Value, FakeYtDlp.PremiumOnly.Id.Value]);
        jobs[free.Id.Value].State.ShouldBe(DownloadState.Completed);
        jobs[free.Id.Value].Title.ShouldBe("Free Song");
        jobs[FakeYtDlp.PremiumOnly.Id.Value].State.ShouldBe(DownloadState.Failed);
        jobs[FakeYtDlp.PremiumOnly.Id.Value].ErrorCode.ShouldBe("youtube.premium_only");

        // The refusal stays on the track, so the app can show it after the download list is empty.
        var playlist = (await client.GetFromJsonAsync<PlaylistResponse>($"/api/playlists/{imported.PlaylistId}", Json, Ct)).ShouldNotBeNull();
        playlist.Entries.Single(e => e.Title == "Free Song").IsAvailableOffline.ShouldBeTrue();
        playlist.Entries.Single(e => e.Title == "Premium Song").DownloadProblem.ShouldBe(DownloadRefusal.PremiumOnly);

        // A second request: nothing is left to get, and the Premium-only track is skipped, not tried again...
        var again = await DownloadPlaylist(client, imported.PlaylistId);
        (again.Queued, again.AlreadyDownloaded, again.Skipped).ShouldBe((0, 1, 1));

        // ...until the request asks to try the skipped tracks again (for example, after you get Premium).
        (await DownloadPlaylist(client, imported.PlaylistId, retrySkipped: true)).Queued.ShouldBe(1);
        await WaitForVideos(client, [FakeYtDlp.PremiumOnly.Id.Value]);
    }

    [Fact]
    public async Task When_youtube_rate_limits_the_queue_rests_then_carries_on_without_counting_a_try()
    {
        using var client = factory.CreateApiClient();
        var youTube = (FakeYtDlp)factory.Services.GetRequiredService<IYtDlp>();
        youTube.RateLimitedTimes["rateLimit01"] = 1;

        string[] videoIds = ["rateLimit01"];
        (await client.PostAsJsonAsync("/api/youtube-music/downloads", new { videoIds }, Ct)).EnsureSuccessStatusCode();

        var sawPause = false;
        for (var i = 0; i < 100 && !sawPause; i++)
        {
            var downloads = (await client.GetFromJsonAsync<DownloadsResponse>("/api/youtube-music/downloads", Json, Ct)).ShouldNotBeNull();
            sawPause = downloads.PausedUntil is not null && downloads.PauseReason!.Contains("429");
            if (!sawPause) await Task.Delay(50, Ct);
        }
        sawPause.ShouldBeTrue("the queue should report that it is resting");

        var done = (await WaitForVideos(client, ["rateLimit01"]))["rateLimit01"];
        done.State.ShouldBe(DownloadState.Completed);
        done.Attempts.ShouldBe(1, "the rate-limited try doesn't count against the video");
    }

    [Fact]
    public async Task A_job_left_running_by_a_dead_worker_is_taken_over_once_its_lease_runs_out()
    {
        using var client = factory.CreateApiClient();
        var past = DateTimeOffset.UtcNow.AddHours(-1);
        var orphan = DownloadJob.Queue(new YouTubeVideoId("orphanSong1"), past);
        orphan.Start(past, TimeSpan.FromMinutes(1)).ShouldBeTrue();   // "Running" since one hour ago. The lease ended long ago.
        await AddJobAndWake(orphan);

        var done = (await WaitForVideos(client, ["orphanSong1"]))["orphanSong1"];
        done.State.ShouldBe(DownloadState.Completed);
        done.Attempts.ShouldBe(2);
    }

    [Fact]
    public async Task A_job_that_never_finishes_is_given_up_after_three_tries()
    {
        using var client = factory.CreateApiClient();
        var past = DateTimeOffset.UtcNow.AddHours(-1);
        var stuck = DownloadJob.Queue(new YouTubeVideoId("stuckSong01"), past);
        for (var attempt = 0; attempt < DownloadJob.MaxAttempts; attempt++)
            stuck.Start(past.AddMinutes(attempt * 2), TimeSpan.FromMinutes(1)).ShouldBeTrue();
        await AddJobAndWake(stuck);

        var done = (await WaitForVideos(client, ["stuckSong01"]))["stuckSong01"];
        done.State.ShouldBe(DownloadState.Failed);
        done.ErrorCode.ShouldBe("download.gave_up");
    }

    private async Task AddJobAndWake(DownloadJob job)
    {
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.DownloadJobs.Add(job);
            await db.SaveChangesAsync(Ct);
        }
        factory.Services.GetRequiredService<DownloadDoorbell>().Ring();
    }

    private static async Task<PlaylistDownloadReport> DownloadPlaylist(HttpClient client, Guid playlistId, bool retrySkipped = false)
    {
        var response = await client.PostAsJsonAsync($"/api/youtube-music/playlists/{playlistId}/downloads", new { retrySkipped }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<PlaylistDownloadReport>(Json, Ct)).ShouldNotBeNull();
    }

    /// <summary>Waits until the newest job of each video is finished. Returns the jobs by video id.</summary>
    private static async Task<Dictionary<string, DownloadStatus>> WaitForVideos(HttpClient client, string[] videoIds)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var downloads = await client.GetFromJsonAsync<DownloadsResponse>("/api/youtube-music/downloads", Json, Ct);
            var newest = downloads!.Jobs.Where(j => videoIds.Contains(j.VideoId))
                .GroupBy(j => j.VideoId).ToDictionary(g => g.Key, g => g.OrderByDescending(j => j.QueuedAt).First());
            if (newest.Count == videoIds.Length && newest.Values.All(j => j.State is DownloadState.Completed or DownloadState.Failed))
                return newest;
            await Task.Delay(100, Ct);
        }
        throw new TimeoutException($"Downloads of {string.Join(", ", videoIds)} did not finish within 20 seconds.");
    }

    private static async Task<DownloadStatus> WaitFor(HttpClient client, Guid jobId)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var downloads = await client.GetFromJsonAsync<DownloadsResponse>("/api/youtube-music/downloads", Json, Ct);
            if (downloads?.Jobs.SingleOrDefault(j => j.JobId == jobId) is { State: DownloadState.Completed or DownloadState.Failed } done) return done;
            await Task.Delay(100, Ct);
        }
        throw new TimeoutException("The download did not finish within 10 seconds.");
    }
}
