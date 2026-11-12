using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OldCountry.Api.Common;
using OldCountry.Api.Features.Tracks;

namespace OldCountry.Api.Tests.Integration;

public sealed class TracksEndpointTests(ApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static object LocalTrack(string title, string path) => new
    {
        title,
        artist = "Old Country Band",
        durationSeconds = 215,
        localPath = path,
        sizeBytes = 31_457_280,
        codec = "Flac",
        bitrateKbps = 1_100,
        sampleRateHz = 44_100,
    };

    [Fact]
    public async Task Adding_a_local_track_returns_201_with_a_location_that_resolves()
    {
        using var client = factory.CreateApiClient();

        var created = await client.PostAsJsonAsync("/api/tracks", LocalTrack("Dust Road", $"/srv/music/{Guid.NewGuid()}.flac"), Ct);

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        created.Headers.Location.ShouldNotBeNull();
        var track = (await created.Content.ReadFromJsonAsync<TrackResponse>(Json, Ct)).ShouldNotBeNull();
        track.Title.ShouldBe("Dust Road");
        track.IsAvailableOffline.ShouldBeTrue();
        track.Codec.ShouldBe(Domain.Library.AudioCodec.Flac);

        var fetched = await client.GetFromJsonAsync<TrackResponse>(created.Headers.Location, Json, Ct);
        fetched.ShouldBe(track);
    }

    [Fact]
    public async Task An_unknown_track_is_a_404_problem_with_a_code()
    {
        using var client = factory.CreateApiClient();

        var response = await client.GetAsync($"/api/tracks/{Guid.CreateVersion7()}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("code").GetString().ShouldBe("track.not_found");
    }

    [Fact]
    public async Task The_same_local_file_twice_is_a_409()
    {
        using var client = factory.CreateApiClient();
        var path = $"/srv/music/{Guid.NewGuid()}.flac";

        (await client.PostAsJsonAsync("/api/tracks", LocalTrack("First", path), Ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
        var second = await client.PostAsJsonAsync("/api/tracks", LocalTrack("Second", path), Ct);

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await second.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe("track.duplicate_file");
    }

    [Fact]
    public async Task A_track_without_any_source_is_a_400_validation_problem()
    {
        using var client = factory.CreateApiClient();

        var response = await client.PostAsJsonAsync("/api/tracks", new { title = "Nowhere", artist = "Nobody", durationSeconds = 60 }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("errors").TryGetProperty("source", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Validators_reject_a_bad_request_before_the_handler_runs()
    {
        using var client = factory.CreateApiClient();

        var response = await client.PostAsJsonAsync("/api/tracks", new { title = "", artist = "Someone", durationSeconds = 0, youTubeVideoId = "short" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_library_lists_in_pages()
    {
        using var client = factory.CreateApiClient();
        for (var i = 0; i < 3; i++)
            (await client.PostAsJsonAsync("/api/tracks", LocalTrack($"Paged {i}", $"/srv/music/{Guid.NewGuid()}.flac"), Ct))
                .StatusCode.ShouldBe(HttpStatusCode.Created);

        var page = await client.GetFromJsonAsync<PagedResponse<TrackResponse>>("/api/tracks?page=1&pageSize=2", Json, Ct);

        page.ShouldNotBeNull();
        page.Items.Count.ShouldBe(2);
        page.PageSize.ShouldBe(2);
        page.TotalCount.ShouldBeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task A_page_size_over_the_limit_is_a_400()
    {
        using var client = factory.CreateApiClient();

        var response = await client.GetAsync("/api/tracks?pageSize=1000", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
