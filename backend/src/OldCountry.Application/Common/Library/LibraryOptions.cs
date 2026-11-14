using OldCountry.Domain.Errors;

namespace OldCountry.Application.Common.Library;

/// <summary>The <c>Library</c> settings section, for example <c>Library__Roots__0=/srv/music</c>.</summary>
public sealed class LibraryOptions
{
    public const string Section = "Library";

    /// <summary>The absolute folders that hold the music. The scan reads all their subfolders. An empty list stops the scans.</summary>
    public IReadOnlyList<string> Roots { get; init; } = [];

    /// <summary>The interval of the automatic scan, at most one week. Zero stops the automatic scans.</summary>
    public TimeSpan RescanInterval { get; init; } = TimeSpan.FromHours(6);

    /// <summary>Do one scan when the server starts.</summary>
    public bool ScanOnStartup { get; init; } = true;

    /// <summary>The reason that the scan cannot read these folders, or <c>null</c> when it can.</summary>
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
