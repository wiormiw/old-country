using OldCountry.Domain.Common;
using OldCountry.Domain.Errors;

namespace OldCountry.Domain.Artists;

/// <summary>A person or group that performs tracks.</summary>
public sealed class Artist : AuditableEntity<ArtistId>
{
    public const int NameMaxLength = 200;

    private Artist() { }

    public string Name { get; private init; } = null!;

    /// <summary>The name without case and outer spaces. "The Band" and " the band" are one artist.</summary>
    public string NormalizedName { get; private init; } = null!;

    public static Artist Create(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length is 0 or > NameMaxLength)
            throw new DomainException("artist.invalid_name", $"An artist name must be 1-{NameMaxLength} characters.");
        return new Artist { Id = ArtistId.New(), Name = trimmed, NormalizedName = Normalize(trimmed) };
    }

    public static string Normalize(string name) => name.Trim().ToUpperInvariant();
}
