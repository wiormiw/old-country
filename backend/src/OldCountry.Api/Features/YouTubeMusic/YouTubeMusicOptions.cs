using FluentValidation;
using OldCountry.Api.Common.Library;
using OldCountry.Domain.Errors;

namespace OldCountry.Api.Features.YouTubeMusic;

/// <summary>The <c>YouTubeMusic</c> configuration section.</summary>
public sealed class YouTubeMusicOptions
{
    public const string Section = "YouTubeMusic";
    public const string DefaultFolderName = "YouTube Music";

    /// <summary>The yt-dlp executable: a name on PATH or an absolute path.</summary>
    public string YtDlpPath { get; init; } = "yt-dlp";

    /// <summary>
    /// A Netscape-format cookies.txt exported from a browser signed in to YouTube Music. Needed
    /// for private playlists and liked songs; public search, streaming and downloads work without.
    /// </summary>
    public string? CookiesFile { get; init; }

    /// <summary>Where downloads go. Defaults to "YouTube Music" inside the first library folder.</summary>
    public string? DownloadFolder { get; init; }

    /// <summary>Audio format downloads are stored in: opus (smallest, YouTube's native), m4a, mp3 or flac.</summary>
    public string AudioFormat { get; init; } = "opus";

    /// <summary>Where audio fetched for streaming is cached. Defaults to a folder under the system temp directory.</summary>
    public string? StreamCacheFolder { get; init; }

    /// <summary>The most space the streaming cache may use; least recently played files go first.</summary>
    public int StreamCacheMaxMegabytes { get; init; } = 2048;

    /// <summary>How long one yt-dlp run may take before it is stopped.</summary>
    public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromMinutes(5);

    public static readonly IReadOnlySet<string> AudioFormats = new HashSet<string>(StringComparer.Ordinal) { "opus", "m4a", "mp3", "flac" };

    /// <summary>
    /// The folder downloads are written to, or why there is none. It must sit inside a library
    /// folder, so downloads can be streamed back and are recognised by library scans.
    /// </summary>
    public Validated<string> ResolveDownloadFolder(LibraryOptions library)
    {
        if (DownloadFolder is null && library.Roots.Count == 0)
            return ValidationError.For("youTubeMusic.downloadFolder",
                "Downloads need a library folder (Library:Roots) or YouTubeMusic:DownloadFolder inside one.");

        var folder = DownloadFolder ?? Path.Combine(library.Roots[0], DefaultFolderName);
        if (!LibraryPaths.IsInsideLibrary(Path.Combine(folder, "probe"), library.Roots))
            return ValidationError.For("youTubeMusic.downloadFolder",
                $"The download folder '{folder}' must be inside one of the library folders (Library:Roots).");
        return folder;
    }
}

internal sealed class YouTubeMusicOptionsValidator : AbstractValidator<YouTubeMusicOptions>
{
    public YouTubeMusicOptionsValidator()
    {
        RuleFor(o => o.YtDlpPath).NotEmpty().WithMessage("YtDlpPath can't be empty; use \"yt-dlp\" to find it on PATH.");
        RuleFor(o => o.CookiesFile!)
            .Must(Path.IsPathFullyQualified).When(o => o.CookiesFile is not null)
            .WithMessage("CookiesFile must be an absolute path to a cookies.txt file.");
        RuleFor(o => o.DownloadFolder!)
            .Must(Path.IsPathFullyQualified).When(o => o.DownloadFolder is not null)
            .WithMessage("DownloadFolder must be an absolute path.");
        RuleFor(o => o.StreamCacheFolder!)
            .Must(Path.IsPathFullyQualified).When(o => o.StreamCacheFolder is not null)
            .WithMessage("StreamCacheFolder must be an absolute path.");
        RuleFor(o => o.StreamCacheMaxMegabytes)
            .InclusiveBetween(64, 1024 * 1024).WithMessage("StreamCacheMaxMegabytes must be between 64 (MB) and 1048576 (1 TB).");
        RuleFor(o => o.AudioFormat)
            .Must(YouTubeMusicOptions.AudioFormats.Contains)
            .WithMessage($"AudioFormat must be one of: {string.Join(", ", YouTubeMusicOptions.AudioFormats)}.");
        RuleFor(o => o.CommandTimeout)
            .InclusiveBetween(TimeSpan.FromSeconds(10), TimeSpan.FromHours(1))
            .WithMessage("CommandTimeout must be between 10 seconds and 1 hour.");
    }
}
