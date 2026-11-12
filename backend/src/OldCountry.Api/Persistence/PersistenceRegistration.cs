using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using OldCountry.Api.Persistence.Interceptors;

namespace OldCountry.Api.Persistence;

internal static class PersistenceRegistration
{
    /// <summary>Matches the database resource name in the Aspire AppHost.</summary>
    public const string ConnectionStringName = "oldcountry";

    public static IHostApplicationBuilder AddPersistence(this IHostApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<AuditableEntityInterceptor>();
        builder.Services.AddDbContext<OldCountryDbContext>((provider, options) => options
            .UseNpgsql(connectionString)
            .AddInterceptors(provider.GetRequiredService<AuditableEntityInterceptor>()));
        return builder;
    }

    /// <summary>Applies pending migrations when <c>Database:MigrateOnStartup</c> is true (Development and tests).</summary>
    public static async Task MigrateDatabaseAsync(this WebApplication app)
    {
        if (!app.Configuration.GetValue<bool>("Database:MigrateOnStartup")) return;
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<OldCountryDbContext>().Database.MigrateAsync();
    }
}

/// <summary>For <c>dotnet ef</c>: builds the model without a running database or host.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<OldCountryDbContext>
{
    public OldCountryDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<OldCountryDbContext>().UseNpgsql("Host=localhost;Database=oldcountry").Options);
}
