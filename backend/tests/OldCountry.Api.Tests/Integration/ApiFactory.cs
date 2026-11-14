using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OldCountry.Application.Abstractions.YouTube;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(OldCountry.Api.Tests.Integration.ApiFactory))]

namespace OldCountry.Api.Tests.Integration;

/// <summary>
/// The real API with a temporary Postgres in Docker. It migrates at startup, the same as Development.
/// When Docker is not available, the integration tests are skipped. They do not fail.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Built in InitializeAsync, because Testcontainers already looks for Docker when it builds the container.
    private PostgreSqlContainer? _postgres;

    public string? DockerUnavailableReason { get; private set; }

    /// <summary>The one library folder of the API. Here the scans are only manual, so the tests decide when they run.</summary>
    public string LibraryRoot { get; } = Directory.CreateTempSubdirectory("old-country-library-").FullName;

    public async ValueTask InitializeAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
            await _postgres.StartAsync();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            DockerUnavailableReason = $"Docker is not available ({exception.GetType().Name}); start it to run integration tests.";
            return;
        }

        // Start the server (and its migrations) one time, here, before the tests run in parallel.
        // Otherwise two tests that ask for a client at the same time would each start a server.
        using var warmUp = CreateClient();
    }

    public HttpClient CreateApiClient()
    {
        Assert.SkipWhen(DockerUnavailableReason is not null, DockerUnavailableReason ?? "");
        return CreateClient();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:oldcountry", _postgres!.GetConnectionString());
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Library:Roots:0", LibraryRoot);
        builder.UseSetting("Library:ScanOnStartup", "false");
        builder.UseSetting("Library:RescanInterval", "00:00:00");
        builder.UseSetting("YouTubeMusic:StreamCacheFolder", Path.Combine(LibraryRoot, ".stream-cache"));
        // No pause between downloads, and a rest of one second for a rate limit, so the tests stay fast.
        builder.UseSetting("YouTubeMusic:DownloadPause", "00:00:00");
        builder.UseSetting("YouTubeMusic:RateLimitPause", "00:00:01");
        builder.UseSetting("YouTubeMusic:PlaylistSyncInterval", "00:00:00");   // The tests run the sync rounds themselves.
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IYtDlp>();
            services.AddSingleton<IYtDlp, FakeYtDlp>();
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (_postgres is not null) await _postgres.DisposeAsync();
        Directory.Delete(LibraryRoot, recursive: true);
    }
}
