using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OldCountry.Domain.Common;
using OldCountry.Domain.Library;

namespace OldCountry.Api.Persistence.Conventions;

/// <summary>
/// Model-wide conversions for single-value records (<c>readonly record struct</c> ids and wrappers).
/// Multi-value records (<see cref="LocalFile"/>, <see cref="AudioQuality"/>) are complex types
/// instead, configured per entity. Record equality is value equality, so the default comparers fit.
/// </summary>
internal static class ValueObjectConverters
{
    public static void Register(ModelConfigurationBuilder builder)
    {
        builder.Properties<TrackId>().HaveConversion<TrackIdConverter>();
        builder.Properties<ArtistId>().HaveConversion<ArtistIdConverter>();
        builder.Properties<PlaylistId>().HaveConversion<PlaylistIdConverter>();
        builder.Properties<YouTubeVideoId>().HaveConversion<YouTubeVideoIdConverter>().HaveMaxLength(YouTubeVideoId.Length);
        // Postgres stores timestamptz as UTC and Npgsql refuses any other offset.
        builder.Properties<DateTimeOffset>().HaveConversion<UtcConverter>();
        builder.Properties<AudioCodec>().HaveConversion<string>().HaveMaxLength(16);
    }

    private sealed class TrackIdConverter() : ValueConverter<TrackId, Guid>(id => id.Value, value => new TrackId(value));

    private sealed class ArtistIdConverter() : ValueConverter<ArtistId, Guid>(id => id.Value, value => new ArtistId(value));

    private sealed class PlaylistIdConverter() : ValueConverter<PlaylistId, Guid>(id => id.Value, value => new PlaylistId(value));

    private sealed class YouTubeVideoIdConverter() : ValueConverter<YouTubeVideoId, string>(id => id.Value, value => new YouTubeVideoId(value));

    private sealed class UtcConverter() : ValueConverter<DateTimeOffset, DateTimeOffset>(value => value.ToUniversalTime(), value => value);
}
