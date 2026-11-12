using Microsoft.EntityFrameworkCore;
using OldCountry.Api.Persistence;
using OldCountry.Domain.Library;

namespace OldCountry.Api.Common.Library;

/// <summary>
/// Finds an artist by name or creates one, shared by every feature that adds tracks. Scoped, so one
/// request (or one library scan) remembers the artists it has already resolved: a scan adding a
/// hundred tracks by one artist creates the artist once, even before it is saved.
/// </summary>
internal sealed partial class ArtistDirectory(OldCountryDbContext db, ILogger<ArtistDirectory> logger)
{
    private readonly Dictionary<string, Artist> _resolved = new(StringComparer.Ordinal);

    public async Task<Artist> FindOrCreateAsync(string name, CancellationToken cancellationToken)
    {
        var normalized = Artist.Normalize(name);
        if (_resolved.TryGetValue(normalized, out var known)) return known;

        var artist = await db.Artists.SingleOrDefaultAsync(a => a.NormalizedName == normalized, cancellationToken);
        if (artist is null)
        {
            artist = Artist.Create(name);
            db.Artists.Add(artist);
            Log.ArtistCreated(logger, artist.Id.Value);
        }

        _resolved[normalized] = artist;
        return artist;
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Created artist {ArtistId}")]
        public static partial void ArtistCreated(ILogger logger, Guid artistId);
    }
}
