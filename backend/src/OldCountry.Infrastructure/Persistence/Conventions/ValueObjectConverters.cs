using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OldCountry.Domain.Artists;
using OldCountry.Domain.Downloads;
using OldCountry.Domain.Playlists;
using OldCountry.Domain.Tracks;
using OldCountry.Domain.YouTube;

namespace OldCountry.Infrastructure.Persistence.Conventions;

/// <summary>
/// Conversions for all records with one value (<c>readonly record struct</c> ids and wrappers), in the full model.
/// Records with many values (<see cref="LocalFile"/>, <see cref="AudioQuality"/>) are complex types,
/// configured for each entity. Record equality is value equality, so the default comparers are correct.
/// </summary>
internal static class ValueObjectConverters
{
    public static void Register(ModelConfigurationBuilder builder)
    {
        builder.Properties<TrackId>().HaveConversion<TrackIdConverter>();
        builder.Properties<ArtistId>().HaveConversion<ArtistIdConverter>();
        builder.Properties<PlaylistId>().HaveConversion<PlaylistIdConverter>();
        builder.Properties<DownloadJobId>().HaveConversion<DownloadJobIdConverter>();
        builder.Properties<YouTubeVideoId>().HaveConversion<YouTubeVideoIdConverter>().HaveMaxLength(YouTubeVideoId.Length);
        // Postgres keeps timestamptz as UTC, and Npgsql refuses other offsets.
        builder.Properties<DateTimeOffset>().HaveConversion<UtcConverter>();
        builder.Properties<AudioCodec>().HaveConversion<string>().HaveMaxLength(16);
        builder.Properties<DownloadRefusal>().HaveConversion<string>().HaveMaxLength(16);
        builder.Properties<DownloadJobState>().HaveConversion<string>().HaveMaxLength(16);
    }

    private sealed class TrackIdConverter() : ValueConverter<TrackId, Guid>(id => id.Value, value => new TrackId(value));

    private sealed class ArtistIdConverter() : ValueConverter<ArtistId, Guid>(id => id.Value, value => new ArtistId(value));

    private sealed class PlaylistIdConverter() : ValueConverter<PlaylistId, Guid>(id => id.Value, value => new PlaylistId(value));

    private sealed class DownloadJobIdConverter() : ValueConverter<DownloadJobId, Guid>(id => id.Value, value => new DownloadJobId(value));

    private sealed class YouTubeVideoIdConverter() : ValueConverter<YouTubeVideoId, string>(id => id.Value, value => new YouTubeVideoId(value));

    private sealed class UtcConverter() : ValueConverter<DateTimeOffset, DateTimeOffset>(value => value.ToUniversalTime(), value => value);
}
