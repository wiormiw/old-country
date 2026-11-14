using Microsoft.EntityFrameworkCore;
using OldCountry.Application.Abstractions.Persistence;
using OldCountry.Infrastructure.Persistence.Interceptors;

namespace OldCountry.Infrastructure.Persistence;

/// <summary>The PostgreSQL database: the context, its interceptors, and the migrations at startup.</summary>
public static class PersistenceRegistration
{
    /// <summary>The name of the connection string. It must be the same as the database resource name in the Aspire AppHost.</summary>
    public const string ConnectionStringName = "oldcountry";

    internal static IHostApplicationBuilder AddPersistence(this IHostApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");

        builder.Services.AddSingleton<AuditableEntityInterceptor>();
        builder.Services.AddSingleton<ChangeNotificationInterceptor>();
        builder.Services.AddDbContext<AppDbContext>((provider, options) => options
            .UseNpgsql(connectionString)
            .AddInterceptors(
                provider.GetRequiredService<AuditableEntityInterceptor>(),
                provider.GetRequiredService<ChangeNotificationInterceptor>()));
        // The use cases get the same scoped context through the interface.
        builder.Services.AddScoped<IAppDbContext>(provider => provider.GetRequiredService<AppDbContext>());
        return builder;
    }

    /// <summary>Applies the pending migrations when <c>Database:MigrateOnStartup</c> is true (Development, tests, Docker).</summary>
    public static async Task MigrateDatabaseAsync(this IHost host)
    {
        if (!host.Services.GetRequiredService<IConfiguration>().GetValue<bool>("Database:MigrateOnStartup")) return;
        await using var scope = host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }
}
