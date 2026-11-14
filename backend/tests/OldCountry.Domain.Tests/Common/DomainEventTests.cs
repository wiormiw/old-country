using OldCountry.Domain.Downloads;
using OldCountry.Domain.Playlists;
using OldCountry.Domain.Tracks;
using OldCountry.Domain.YouTube;

namespace OldCountry.Domain.Tests.Common;

public sealed class DomainEventTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly YouTubeVideoId Video = new("video000001");

    private static DownloadJob Running()
    {
        var job = DownloadJob.Queue(Video, Now);
        job.Start(Now, TimeSpan.FromMinutes(5)).ShouldBeTrue();
        return job;
    }

    [Fact]
    public void A_finished_download_says_which_song_it_became()
    {
        var job = Running();
        var track = TrackId.New();

        job.Complete(track, Now);

        job.DomainEvents.ShouldBe([new DownloadFinished(job.Id, Video, track)]);
    }

    [Fact]
    public void A_refused_or_abandoned_download_says_why()
    {
        var refused = Running();
        refused.Fail("youtube.premium_only", "Premium only", Now);
        refused.DomainEvents.ShouldBe([new DownloadFailed(refused.Id, Video, "youtube.premium_only", "Premium only")]);

        var abandoned = DownloadJob.Queue(Video, Now);
        for (var i = 0; i <= DownloadJob.MaxAttempts; i++) abandoned.Start(Now + TimeSpan.FromHours(i), TimeSpan.FromMinutes(5));
        abandoned.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<DownloadFailed>().Code.ShouldBe("download.gave_up");
    }

    [Fact]
    public void Songs_from_youtube_are_announced_only_when_there_are_some()
    {
        var playlist = Playlist.Create("Mix", null).Value.ShouldBeOfType<Playlist>();

        playlist.AppendFromYouTube([], Now).ShouldBeNull();
        playlist.DomainEvents.ShouldBeEmpty();

        playlist.AppendFromYouTube([(Video, TrackId.New()), (new YouTubeVideoId("video000002"), TrackId.New())], Now).ShouldBeNull();
        playlist.DomainEvents.ShouldBe([new SongsAddedFromYouTube(playlist.Id, 2)]);
    }

    [Fact]
    public void Persistence_clears_events_once_it_has_taken_them()
    {
        var job = Running();
        job.Complete(TrackId.New(), Now);

        job.ClearDomainEvents();

        job.DomainEvents.ShouldBeEmpty();
    }
}
