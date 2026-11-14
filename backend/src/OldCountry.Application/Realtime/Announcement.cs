namespace OldCountry.Application.Realtime;

/// <summary>
/// News for the user of the apps, sent with the change notices: "Downloaded “Song”".
/// The apps show <see cref="Message"/> as it is. The server decides the one text.
/// The ids let the apps link to the item.
/// </summary>
public sealed record Announcement(AnnouncementKind Kind, string Message, Guid? TrackId = null, Guid? PlaylistId = null)
{
    /// <summary>When more news of one type than this arrives together, it becomes one summary.</summary>
    public const int SummarizeAbove = 3;

    /// <summary>Tells a burst ("Download all" that finishes tracks one after another) as one line for each type.</summary>
    public static IReadOnlyList<Announcement> Summarize(IReadOnlyList<Announcement> news) =>
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
