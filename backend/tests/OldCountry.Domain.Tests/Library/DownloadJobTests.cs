using OldCountry.Domain.Common;
using OldCountry.Domain.Library;

namespace OldCountry.Domain.Tests.Library;

public sealed class DownloadJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(12);
    private static readonly YouTubeVideoId Video = new("video000001");

    [Fact]
    public void A_started_job_belongs_to_its_worker_until_the_lease_runs_out()
    {
        var job = DownloadJob.Queue(Video, Now);
        job.IsClaimable(Now).ShouldBeTrue();

        job.Start(Now, Lease).ShouldBeTrue();

        job.State.ShouldBe(DownloadJobState.Running);
        job.Attempts.ShouldBe(1);
        job.IsClaimable(Now + Lease - TimeSpan.FromSeconds(1)).ShouldBeFalse("its worker may still be downloading");
        job.Start(Now + TimeSpan.FromMinutes(1), Lease).ShouldBeFalse();
        job.IsClaimable(Now + Lease).ShouldBeTrue("its worker is presumed dead");
        job.Start(Now + Lease, Lease).ShouldBeTrue();
        job.Attempts.ShouldBe(2);
    }

    [Fact]
    public void After_three_starts_that_never_finished_the_job_is_given_up()
    {
        var job = DownloadJob.Queue(Video, Now);
        for (var i = 0; i < DownloadJob.MaxAttempts; i++) job.Start(Now + Lease * i, Lease).ShouldBeTrue();

        job.Start(Now + Lease * DownloadJob.MaxAttempts, Lease).ShouldBeFalse();

        job.State.ShouldBe(DownloadJobState.Failed);
        job.ErrorCode.ShouldBe("download.gave_up");
        job.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void Finishing_releases_the_lease_and_records_the_outcome()
    {
        var done = DownloadJob.Queue(Video, Now);
        done.Start(Now, Lease);
        var track = TrackId.New();
        done.Complete(track, Now + TimeSpan.FromMinutes(1));
        (done.State, done.TrackId, done.LeaseUntil, done.FinishedAt).ShouldBe(
            (DownloadJobState.Completed, track, null, Now + TimeSpan.FromMinutes(1)));

        var refused = DownloadJob.Queue(Video, Now);
        refused.Start(Now, Lease);
        refused.Fail("youtube.premium_only", "Premium members only", Now);
        (refused.State, refused.ErrorCode).ShouldBe((DownloadJobState.Failed, "youtube.premium_only"));
    }

    [Fact]
    public void A_postponed_job_goes_back_in_the_queue_without_using_up_a_try()
    {
        var job = DownloadJob.Queue(Video, Now);
        job.Start(Now, Lease);

        job.Postpone();

        (job.State, job.Attempts, job.LeaseUntil).ShouldBe((DownloadJobState.Queued, 0, null));
        job.IsClaimable(Now).ShouldBeTrue();
    }

    [Fact]
    public void A_track_remembers_a_refused_download_until_one_succeeds()
    {
        var track = (Track)Track.Create("Song", ArtistId.New(), TimeSpan.FromMinutes(3), null, Video).Value!;
        track.RecordDownloadProblem(DownloadProblem.Of(DownloadRefusal.PremiumOnly, "Premium members only", Now));
        track.DownloadProblem!.IsLasting.ShouldBeTrue();

        track.AttachLocalFile((LocalFile)LocalFile.Create("/music/song.opus", 1000, new AudioQuality(AudioCodec.Opus, 160, 48_000)).Value!);

        track.DownloadProblem.ShouldBeNull();
        DownloadProblem.Of(DownloadRefusal.Failed, "x", Now).IsLasting.ShouldBeFalse("a one-off failure is worth another try");
    }
}
