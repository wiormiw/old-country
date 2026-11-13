using System.Net;
using System.Net.Http.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using OldCountry.Api.Common.Realtime;
using OldCountry.Api.Features.Playlists;

namespace OldCountry.Api.Tests.Integration;

/// <summary>
/// The apps' live updates, end to end: a real SignalR client connected over WebSockets (as the
/// desktop app connects: no negotiate call), changes made through the HTTP API.
/// Tests share one server, so each looks only for the changes it made.
/// </summary>
public sealed class LibraryHubTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Long enough for the broadcaster's merge window, short enough to keep tests quick.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private sealed class Listener(HubConnection connection, Channel<LibraryChanges> heard) : IAsyncDisposable
    {
        /// <summary>The first change matching <paramref name="wanted"/>, or null if none comes in time.</summary>
        public async Task<LibraryChanges?> WaitFor(Func<LibraryChanges, bool> wanted, TimeSpan within)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            timeout.CancelAfter(within);
            try
            {
                while (true)
                {
                    var changes = await heard.Reader.ReadAsync(timeout.Token);
                    if (wanted(changes)) return changes;
                }
            }
            catch (OperationCanceledException) when (!Ct.IsCancellationRequested)
            {
                return null;
            }
        }

        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }

    private async Task<Listener> Listen()
    {
        using var _ = factory.CreateApiClient(); // skips the test when Docker isn't running
        var heard = Channel.CreateUnbounded<LibraryChanges>();
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, LibraryHub.Path), options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.SkipNegotiation = true;
                options.WebSocketFactory = async (context, cancellationToken) =>
                    await factory.Server.CreateWebSocketClient().ConnectAsync(context.Uri, cancellationToken);
            })
            .Build();
        connection.On<LibraryChanges>(nameof(ILibraryClient.Changed), changes => heard.Writer.TryWrite(changes));
        await connection.StartAsync(Ct);
        return new Listener(connection, heard);
    }

    [Fact]
    public async Task Apps_hear_about_a_playlist_made_and_renamed_through_the_api()
    {
        await using var listener = await Listen();
        using var client = factory.CreateApiClient();

        var created = await client.PostAsJsonAsync("/api/playlists", new { name = "Heard", description = (string?)null }, Ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = (await created.Content.ReadFromJsonAsync<PlaylistSummaryResponse>(Ct)).ShouldNotBeNull().Id;
        (await listener.WaitFor(c => c.Playlists.Contains(id), Patience)).ShouldNotBeNull("creating it");

        var renamed = await client.PutAsJsonAsync($"/api/playlists/{id}", new { name = "Heard again", description = "still" }, Ct);
        renamed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await listener.WaitFor(c => c.Playlists.Contains(id), Patience)).ShouldNotBeNull("renaming it");
    }

    [Fact]
    public async Task A_refused_change_tells_nobody()
    {
        await using var listener = await Listen();
        using var client = factory.CreateApiClient();
        var created = await client.PostAsJsonAsync("/api/playlists", new { name = "Quiet", description = (string?)null }, Ct);
        var id = (await created.Content.ReadFromJsonAsync<PlaylistSummaryResponse>(Ct)).ShouldNotBeNull().Id;
        (await listener.WaitFor(c => c.Playlists.Contains(id), Patience)).ShouldNotBeNull();

        // A track that doesn't exist: the command refuses inside its transaction, which rolls back.
        var refused = await client.PostAsJsonAsync($"/api/playlists/{id}/tracks", new { trackIds = new[] { Guid.NewGuid() } }, Ct);
        refused.IsSuccessStatusCode.ShouldBeFalse();

        (await listener.WaitFor(c => c.Playlists.Contains(id), TimeSpan.FromSeconds(2))).ShouldBeNull();
    }

    [Fact]
    public async Task Apps_hear_about_new_songs()
    {
        await using var listener = await Listen();
        using var client = factory.CreateApiClient();

        var added = await client.PostAsJsonAsync("/api/tracks", new
        {
            title = "Live",
            artist = "Hub Band",
            durationSeconds = 120,
            localPath = $"/srv/music/{Guid.NewGuid()}.flac",
            sizeBytes = 1_000,
        }, Ct);
        added.StatusCode.ShouldBe(HttpStatusCode.Created);

        (await listener.WaitFor(c => c.Tracks, Patience)).ShouldNotBeNull();
    }
}
