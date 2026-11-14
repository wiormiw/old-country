using OldCountry.Domain.Errors;

namespace OldCountry.Domain.YouTube;

/// <summary>The 11-character id that YouTube and YouTube Music give to each video.</summary>
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
