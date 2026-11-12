using OldCountry.Domain.Errors;

namespace OldCountry.Domain.Library;

public enum AudioCodec
{
    Unknown = 0,
    Flac = 1,
    Mp3 = 2,
    Aac = 3,
    Opus = 4,
    Vorbis = 5,
    Alac = 6,
    Wav = 7,
}

/// <summary>How a file was encoded. Stored inline with its track as a complex type.</summary>
public sealed record AudioQuality(AudioCodec Codec, int BitrateKbps, int SampleRateHz)
{
    public bool IsLossless => Codec is AudioCodec.Flac or AudioCodec.Alac or AudioCodec.Wav;
}

/// <summary>A copy of the track on the server's disk, playable offline.</summary>
public sealed record LocalFile(string Path, long SizeBytes, AudioQuality Quality)
{
    // Materialization: a nested record (Quality) can't be bound through a constructor parameter,
    // so the persistence layer creates an empty instance and sets the init-only properties.
    private LocalFile() : this(null!, 0, null!) { }

    public static Validated<LocalFile> Create(string path, long sizeBytes, AudioQuality quality)
    {
        if (string.IsNullOrWhiteSpace(path) || !System.IO.Path.IsPathFullyQualified(path))
            return ValidationError.For("localPath", "Must be an absolute path.");
        if (sizeBytes <= 0)
            return ValidationError.For("sizeBytes", "Must be greater than zero.");
        return new LocalFile(path, sizeBytes, quality);
    }
}

/// <summary>The 11-character id YouTube (and YouTube Music) gives every video.</summary>
public readonly record struct YouTubeVideoId(string Value)
{
    public const int Length = 11;

    public static Validated<YouTubeVideoId> Create(string? value)
    {
        if (value is not { Length: Length } || !value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            return ValidationError.For("youTubeVideoId", "Must be 11 characters of letters, digits, '-' or '_'.");
        return new YouTubeVideoId(value);
    }

    public override string ToString() => Value;
}
