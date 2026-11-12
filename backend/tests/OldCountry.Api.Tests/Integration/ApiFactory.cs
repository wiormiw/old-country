using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OldCountry.Api.Features.YouTubeMusic;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(OldCountry.Api.Tests.Integration.ApiFactory))]

namespace OldCountry.Api.Tests.Integration;

/// <summary>
/// The real API against a throwaway Postgres in Docker, migrated on startup like Development.
/// When Docker isn't reachable the integration tests are skipped instead of failing.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Built in InitializeAsync: Testcontainers probes Docker already when building the container.
    private PostgreSqlContainer? _postgres;

    public string? DockerUnavailableReason { get; private set; }

    /// <summary>The API's only library folder. Scans are manual-only here, so tests decide when they run.</summary>
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
        }
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
