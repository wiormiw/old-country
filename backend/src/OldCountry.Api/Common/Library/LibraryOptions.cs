using FluentValidation;
using OldCountry.Domain.Errors;

namespace OldCountry.Api.Common.Library;

/// <summary>The <c>Library</c> configuration section, e.g. <c>Library__Roots__0=/srv/music</c>.</summary>
public sealed class LibraryOptions
{
    public const string Section = "Library";

    /// <summary>Absolute folders holding the music, scanned recursively. Empty disables scanning.</summary>
    public IReadOnlyList<string> Roots { get; init; } = [];

    /// <summary>How often to rescan on its own, at most a week. Zero turns scheduled scans off.</summary>
    public TimeSpan RescanInterval { get; init; } = TimeSpan.FromHours(6);

    /// <summary>Scan once when the server starts.</summary>
    public bool ScanOnStartup { get; init; } = true;

    /// <summary>Why these roots can't be scanned, or <c>null</c> when they can.</summary>
    public static ValidationError? Check(IReadOnlyList<string> roots)
    {
        if (roots.Count == 0)
            return ValidationError.For("library.roots", "No library folders are configured (Library:Roots).");
        foreach (var root in roots)
        {
            if (!Path.IsPathFullyQualified(root) || !Directory.Exists(root))
                return ValidationError.For("library.roots", $"'{root}' is not an existing absolute folder.");
        }
        return null;
    }
}

/// <summary>
/// Checked once at startup, so a typo in configuration stops the server with a clear message
/// instead of surfacing at the first scan. Whether a folder exists is checked at scan time,
/// since a disk may be mounted after the server starts.
/// </summary>
internal sealed class LibraryOptionsValidator : AbstractValidator<LibraryOptions>
{
    public LibraryOptionsValidator()
    {
        RuleForEach(o => o.Roots)
            .Must(Path.IsPathFullyQualified).WithMessage("Library folder '{PropertyValue}' must be an absolute path.");
        RuleFor(o => o.RescanInterval)
            .InclusiveBetween(TimeSpan.Zero, TimeSpan.FromDays(7))
            .WithMessage("RescanInterval must be between 00:00:00 (off) and 7.00:00:00 (a week).");
    }
}

/// <summary>Path checks that keep the API from serving or writing files outside the library.</summary>
internal static class LibraryPaths
{
    /// <summary>
    /// Whether <paramref name="path"/> (and, for a symbolic link, the file it finally points to)
    /// lies inside one of the library folders. ".." segments are resolved first.
    /// </summary>
    public static bool IsInsideLibrary(string path, IReadOnlyList<string> roots)
    {
        if (!Path.IsPathFullyQualified(path)) return false;
        var full = Path.GetFullPath(path);
        if (!IsUnderAny(full, roots)) return false;

        var target = File.Exists(full) ? File.ResolveLinkTarget(full, returnFinalTarget: true)?.FullName : null;
        return target is null || IsUnderAny(target, roots);
    }

    private static bool IsUnderAny(string fullPath, IReadOnlyList<string> roots) =>
        roots.Any(root =>
        {
            var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(prefix, StringComparison.Ordinal);
        });
}
