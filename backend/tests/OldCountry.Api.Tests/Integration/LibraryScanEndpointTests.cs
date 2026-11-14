using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using OldCountry.Application.Common.Paging;
using OldCountry.Application.Tracks;
using OldCountry.Application.Tracks.ScanLibrary;
using OldCountry.Tests.Shared;

namespace OldCountry.Api.Tests.Integration;

public sealed class LibraryScanEndpointTests(ApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_manual_scan_adds_new_audio_files_once()
    {
        using var client = factory.CreateApiClient();
        var album = Path.Combine(factory.LibraryRoot, $"album-{Guid.NewGuid():N}");
        var path = TestAudio.WriteSilentWav(album, "Field Recording.wav", seconds: 3);
        File.WriteAllText(Path.Combine(album, "cover.txt"), "not audio");

        var first = await ScanAndWait(client);
        first.Added.ShouldBeGreaterThanOrEqualTo(1);

        var page = await client.GetFromJsonAsync<PagedResponse<TrackResponse>>("/api/tracks?pageSize=200", Json, Ct);
        var track = page.ShouldNotBeNull().Items.Where(t => t.Title == "Field Recording").ShouldHaveSingleItem();
        track.Artist.ShouldBe("Unknown Artist");
        track.IsAvailableOffline.ShouldBeTrue();
        track.Duration.TotalSeconds.ShouldBe(3, tolerance: 0.1);

        // A second scan finds that the file is known and adds nothing for it.
        var second = await ScanAndWait(client);
        second.Added.ShouldBe(0);
        second.AlreadyKnown.ShouldBeGreaterThanOrEqualTo(1);

        File.Delete(path);
    }

    private static async Task<ScanReport> ScanAndWait(HttpClient client)
    {
        var requestedAt = DateTimeOffset.UtcNow;
        var accepted = await client.PostAsync("/api/tracks/scans", content: null, Ct);
        accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        accepted.Headers.Location.ShouldNotBeNull();

        for (var attempt = 0; attempt < 100; attempt++)
        {
            var status = await client.GetFromJsonAsync<ScanStatusResponse>(accepted.Headers.Location, Json, Ct);
            if (status is { IsRunning: false, IsQueued: false, LastScan: { } report } && report.StartedAt >= requestedAt.AddSeconds(-1))
                return report;
            await Task.Delay(100, Ct);
        }
        throw new TimeoutException("The library scan did not finish within 10 seconds.");
    }
}
