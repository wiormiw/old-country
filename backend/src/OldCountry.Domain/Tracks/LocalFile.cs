using OldCountry.Domain.Errors;

namespace OldCountry.Domain.Tracks;

/// <summary>A copy of the track on the disk of the server. The track can play without a connection.</summary>
public sealed record LocalFile(string Path, long SizeBytes, AudioQuality Quality)
{
    // EF Core cannot bind a nested record (Quality) through a constructor parameter.
    // Thus it makes an empty instance and then sets the init-only properties.
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
