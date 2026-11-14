namespace OldCountry.Application.Tracks.ScanLibrary;

/// <summary>The result of one scan.</summary>
/// <param name="Trigger">What started the scan.</param>
/// <param name="FilesSeen">The audio files in the library folders.</param>
/// <param name="Added">The new tracks that the scan added to the library.</param>
/// <param name="AlreadyKnown">The files that were already in the library.</param>
/// <param name="Unreadable">The files that the scan skipped, because it could not read their audio or tags.</param>
/// <param name="Missing">Library tracks with a file that is not on the disk now. The scan keeps them. It does not delete them.</param>
/// <param name="StartedAt">When the scan started.</param>
/// <param name="FinishedAt">When the scan finished.</param>
public sealed record ScanReport(
    ScanTrigger Trigger,
    int FilesSeen,
    int Added,
    int AlreadyKnown,
    int Unreadable,
    int Missing,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt);
