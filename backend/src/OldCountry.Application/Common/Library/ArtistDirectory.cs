using Microsoft.EntityFrameworkCore;
using OldCountry.Application.Abstractions.Persistence;
using OldCountry.Domain.Artists;

namespace OldCountry.Application.Common.Library;

/// <summary>
/// Finds an artist by name, or makes a new one. All features that add tracks use it.
/// It is scoped, so one request (or one library scan) keeps the artists that it found.
/// Thus a scan that adds 100 tracks by one artist makes the artist one time, also before the save.
/// </summary>
internal partial class ArtistDirectory(IAppDbContext db, ILogger<ArtistDirectory> logger)
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
