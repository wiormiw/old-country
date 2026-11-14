using OldCountry.Application.Realtime;

namespace OldCountry.Application.Tests.Realtime;

public sealed class AnnouncementTests
{
    private static Announcement Downloaded(string title) => new(AnnouncementKind.Downloaded, $"Downloaded “{title}”.");

    [Fact]
    public void A_few_announcements_go_out_as_they_are()
    {
        IReadOnlyList<Announcement> news = [Downloaded("A"), Downloaded("B"), new(AnnouncementKind.NewFromYouTube, "“Mix”: 2 new songs from YouTube.")];

        Announcement.Summarize(news).ShouldBe(news);
    }

    [Fact]
    public void A_burst_of_one_kind_becomes_one_line()
    {
        IReadOnlyList<Announcement> news = [.. Enumerable.Range(1, 12).Select(i => Downloaded($"Song {i}")), new(AnnouncementKind.DownloadFailed, "YouTube refused “X”: Premium only.")];

        var summary = Announcement.Summarize(news);

        summary.Count.ShouldBe(2);
        summary.ShouldContain(new Announcement(AnnouncementKind.Downloaded, "Downloaded 12 songs."));
        summary.ShouldContain(a => a.Kind == AnnouncementKind.DownloadFailed && a.Message.Contains("Premium only"));
    }
}
