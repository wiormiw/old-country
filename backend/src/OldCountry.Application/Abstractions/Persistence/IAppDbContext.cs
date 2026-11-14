using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OldCountry.Domain.Artists;
using OldCountry.Domain.Downloads;
using OldCountry.Domain.Playlists;
using OldCountry.Domain.Tracks;

namespace OldCountry.Application.Abstractions.Persistence;

/// <summary>
/// The database, as the use cases see it. There are no repositories: a handler queries these sets directly.
/// The context is also the unit of work. <c>SaveChangesAsync</c> writes all tracked changes together.
/// </summary>
public interface IAppDbContext
{
    DbSet<Track> Tracks { get; }

    DbSet<Artist> Artists { get; }

    DbSet<Playlist> Playlists { get; }

    DbSet<DownloadJob> DownloadJobs { get; }

    /// <summary>Transactions, for the transaction decorator and for the download claim.</summary>
    DatabaseFacade Database { get; }

    /// <summary>The tracked entities. The library scan clears it between batches.</summary>
    ChangeTracker ChangeTracker { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
