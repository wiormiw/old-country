using Microsoft.EntityFrameworkCore;
using OldCountry.Api.Persistence.Conventions;
using OldCountry.Domain.Common;
using OldCountry.Domain.Library;

namespace OldCountry.Api.Persistence;

/// <summary>
/// No repositories in front of it: each use case queries the context directly. Value objects are
/// mapped here, never annotated in the Domain.
/// </summary>
public sealed class OldCountryDbContext(DbContextOptions<OldCountryDbContext> options) : DbContext(options)
{
    public DbSet<Track> Tracks => Set<Track>();

    public DbSet<Artist> Artists => Set<Artist>();

    public DbSet<Playlist> Playlists => Set<Playlist>();

    public DbSet<DownloadJob> DownloadJobs => Set<DownloadJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OldCountryDbContext).Assembly);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Conventions.Add(_ => new SnakeCaseNamingConvention());
        // Single-value value objects become their primitive column, everywhere they appear.
        ValueObjectConverters.Register(configurationBuilder);
        // Domain events live on entities only until the save that commits them.
        configurationBuilder.IgnoreAny<IReadOnlyList<IDomainEvent>>();
    }
}
