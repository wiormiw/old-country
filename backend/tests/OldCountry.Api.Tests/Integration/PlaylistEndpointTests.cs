using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using OldCountry.Api.Features.Playlists;
using OldCountry.Api.Features.Tracks;

namespace OldCountry.Api.Tests.Integration;

public sealed class PlaylistEndpointTests(ApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<Guid> AddTrack(HttpClient client, string title)
    {
        var response = await client.PostAsJsonAsync("/api/tracks", new
        {
            title,
            artist = "Playlist Band",
            durationSeconds = 100,
            localPath = $"/srv/music/{Guid.NewGuid()}.flac",
            sizeBytes = 1_000,
        }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<TrackResponse>(Json, Ct)).ShouldNotBeNull().Id;
    }

    private static async Task<PlaylistResponse> Get(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<PlaylistResponse>($"/api/playlists/{id}", Json, Ct)).ShouldNotBeNull();

    [Fact]
    public async Task A_playlist_is_built_reordered_trimmed_renamed_and_deleted()
    {
        using var client = factory.CreateApiClient();
        var one = await AddTrack(client, "One");
        var two = await AddTrack(client, "Two");
        var three = await AddTrack(client, "Three");

        var created = await client.PostAsJsonAsync("/api/playlists", new { name = "Road trip" }, Ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = (await created.Content.ReadFromJsonAsync<PlaylistSummaryResponse>(Json, Ct)).ShouldNotBeNull().Id;

        var added = await client.PostAsJsonAsync($"/api/playlists/{id}/tracks", new { trackIds = new[] { one, two, three, one } }, Ct);
        (await added.Content.ReadFromJsonAsync<PlaylistSummaryResponse>(Json, Ct)).ShouldNotBeNull().TrackCount.ShouldBe(4);

        var playlist = await Get(client, id);
        playlist.Entries.Select(e => e.Title).ShouldBe(["One", "Two", "Three", "One"]);
        playlist.Entries.Select(e => e.Position).ShouldBe([0, 1, 2, 3]);
        playlist.TotalDuration.ShouldBe(TimeSpan.FromSeconds(400));

        (await client.PostAsJsonAsync($"/api/playlists/{id}/tracks/move", new { from = 2, to = 0 }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.DeleteAsync($"/api/playlists/{id}/tracks/3", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Get(client, id)).Entries.Select(e => e.Title).ShouldBe(["Three", "One", "Two"]);

        var renamed = await client.PutAsJsonAsync($"/api/playlists/{id}", new { name = "Night drive", description = "quiet" }, Ct);
        var summary = (await renamed.Content.ReadFromJsonAsync<PlaylistSummaryResponse>(Json, Ct)).ShouldNotBeNull();
        summary.Name.ShouldBe("Night drive");
        summary.UpdatedAt.ShouldNotBeNull();

        (await client.DeleteAsync($"/api/playlists/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetAsync($"/api/playlists/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/tracks/{one}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Adding_an_unknown_track_refuses_the_whole_request()
    {
        using var client = factory.CreateApiClient();
        var known = await AddTrack(client, "Known");
        var created = await client.PostAsJsonAsync("/api/playlists", new { name = "Strict" }, Ct);
        var id = (await created.Content.ReadFromJsonAsync<PlaylistSummaryResponse>(Json, Ct)).ShouldNotBeNull().Id;

        var response = await client.PostAsJsonAsync($"/api/playlists/{id}/tracks", new { trackIds = new[] { known, Guid.CreateVersion7() } }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Get(client, id)).Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task Positions_that_do_not_exist_are_404s()
    {
        using var client = factory.CreateApiClient();
        var created = await client.PostAsJsonAsync("/api/playlists", new { name = "Empty" }, Ct);
        var id = (await created.Content.ReadFromJsonAsync<PlaylistSummaryResponse>(Json, Ct)).ShouldNotBeNull().Id;

        (await client.DeleteAsync($"/api/playlists/{id}/tracks/0", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.PostAsJsonAsync($"/api/playlists/{id}/tracks/move", new { from = 0, to = 1 }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
