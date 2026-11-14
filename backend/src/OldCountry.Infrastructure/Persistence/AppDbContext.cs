using Microsoft.EntityFrameworkCore;
using OldCountry.Application.Abstractions.Persistence;
using OldCountry.Domain.Artists;
using OldCountry.Domain.Common;
using OldCountry.Domain.Downloads;
using OldCountry.Domain.Playlists;
using OldCountry.Domain.Tracks;
using OldCountry.Infrastructure.Persistence.Conventions;

namespace OldCountry.Infrastructure.Persistence;

/// <summary>
/// The EF Core context for PostgreSQL. The use cases query it through <see cref="IAppDbContext"/>, with no repositories.
/// The mapping of value objects is here. The Domain has no persistence attributes.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<Track> Tracks => Set<Track>();

    public DbSet<Artist> Artists => Set<Artist>();

    public DbSet<Playlist> Playlists => Set<Playlist>();

    public DbSet<DownloadJob> DownloadJobs => Set<DownloadJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Conventions.Add(_ => new SnakeCaseNamingConvention());
        // A value object with one value becomes a column of its primitive type, in all entities.
        ValueObjectConverters.Register(configurationBuilder);
        // Domain events stay on an entity only until the save that commits them. The database does not keep them.
        configurationBuilder.IgnoreAny<IReadOnlyList<IDomainEvent>>();
    }
}
