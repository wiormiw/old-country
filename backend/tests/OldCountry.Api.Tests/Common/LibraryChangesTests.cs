using OldCountry.Api.Common.Realtime;

namespace OldCountry.Api.Tests.Common;

public sealed class LibraryChangesTests
{
    [Fact]
    public void Merging_keeps_every_playlist_once_and_every_flag()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var merged = new LibraryChanges([a], Tracks: true, Downloads: false)
            .Merge(new LibraryChanges([a, b], Tracks: false, Downloads: true));

        merged.Playlists.ShouldBe([a, b], ignoreOrder: true);
        merged.Tracks.ShouldBeTrue();
        merged.Downloads.ShouldBeTrue();
        merged.Everything.ShouldBeFalse();
    }

    [Fact]
    public void Nothing_is_empty_and_anything_is_not()
    {
        LibraryChanges.None.IsEmpty.ShouldBeTrue();
        (LibraryChanges.None with { Downloads = true }).IsEmpty.ShouldBeFalse();
        (LibraryChanges.None with { Everything = true }).IsEmpty.ShouldBeFalse();
    }

    [Fact]
    public void A_feed_that_dropped_changes_says_so_once()
    {
        var feed = new ChangeFeed();
        for (var i = 0; i < 1_001; i++) feed.Publish(new LibraryChanges([Guid.NewGuid()], false, false));

        feed.TakeOverflow().ShouldBeTrue();
        feed.TakeOverflow().ShouldBeFalse();
    }
}

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
