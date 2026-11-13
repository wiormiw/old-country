namespace OldCountry.Api.Common.Realtime;

public enum AnnouncementKind
{
    /// <summary>A YouTube download finished; the song plays from the library now.</summary>
    Downloaded,

    /// <summary>A YouTube download ended without a song (refused or failed).</summary>
    DownloadFailed,

    /// <summary>Songs arrived in a playlist from its YouTube Music playlist.</summary>
    NewFromYouTube,
}

/// <summary>
/// News for the person using the apps, sent alongside the refetch hints: "Downloaded “Song”".
/// Apps show <see cref="Message"/> as it is (one wording, decided here); the ids let them link
/// to what it's about.
/// </summary>
public sealed record Announcement(AnnouncementKind Kind, string Message, Guid? TrackId = null, Guid? PlaylistId = null)
{
    /// <summary>More of one kind than this, arriving together, become a single summary.</summary>
    internal const int SummarizeAbove = 3;

    /// <summary>A burst ("Download all" finishing songs back to back) told as one line per kind.</summary>
    internal static IReadOnlyList<Announcement> Summarize(IReadOnlyList<Announcement> news) =>
        news.GroupBy(a => a.Kind)
            .SelectMany(IEnumerable<Announcement> (kind) => kind.Count() <= SummarizeAbove
                ? kind
                : [new Announcement(kind.Key, Summary(kind.Key, kind.Count()))])
            .ToArray();

    private static string Summary(AnnouncementKind kind, int count) => kind switch
    {
        AnnouncementKind.Downloaded => $"Downloaded {count} songs.",
        AnnouncementKind.DownloadFailed => $"{count} downloads didn't finish. See Downloads.",
        _ => $"New songs from YouTube in {count} playlists.",
    };
}
