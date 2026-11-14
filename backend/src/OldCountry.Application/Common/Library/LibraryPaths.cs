namespace OldCountry.Application.Common.Library;

/// <summary>Path checks that stop the API when it would serve or write files outside the library.</summary>
internal static class LibraryPaths
{
    /// <summary>
    /// True when <paramref name="path"/> is in one of the library folders.
    /// For a symbolic link, its last target must also be in a library folder. The check resolves ".." first.
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
