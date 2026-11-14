using OldCountry.Application.Common.Library;
using OldCountry.Application.Tracks.ScanLibrary;
using OldCountry.Domain.Errors;
using OldCountry.Domain.Tracks;
using OldCountry.Tests.Shared;

namespace OldCountry.Application.Tests.Tracks;

/// <summary>The parts of the library scan that need no database.</summary>
public sealed class ScanLibraryTests : IDisposable
{
    private readonly string _directory = TestAudio.NewTempDirectory();

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Theory]
    [InlineData("a.FLAC", true, AudioCodec.Flac)]
    [InlineData("b.mp3", true, AudioCodec.Mp3)]
    [InlineData("c.opus", true, AudioCodec.Opus)]
    [InlineData("cover.jpg", false, AudioCodec.Unknown)]
    [InlineData("notes", false, AudioCodec.Unknown)]
    public void Picks_audio_files_by_extension(string name, bool isAudio, AudioCodec expected)
    {
        AudioFormats.TryGetCodec(name, out var codec).ShouldBe(isAudio);
        codec.ShouldBe(expected);
    }

    [Fact]
    public void Options_without_roots_are_refused() =>
        LibraryOptions.Check([]).ShouldBeOfType<ValidationError>().Errors.ShouldContainKey("library.roots");

    [Fact]
    public void Options_with_a_relative_or_missing_root_are_refused()
    {
        LibraryOptions.Check(["music"]).ShouldNotBeNull();
        LibraryOptions.Check([Path.Combine(_directory, "does-not-exist")]).ShouldNotBeNull();
    }

    [Fact]
    public void Options_with_an_existing_absolute_root_pass() =>
        LibraryOptions.Check([_directory]).ShouldBeNull();

    [Fact]
    public void Scan_requests_coalesce_while_one_is_waiting()
    {
        var queue = new LibraryScanQueue();

        queue.Request(ScanTrigger.Manual).ShouldBeTrue();
        queue.Request(ScanTrigger.Manual).ShouldBeFalse();
        queue.Request(ScanTrigger.Scheduled).ShouldBeFalse();
        queue.IsQueued.ShouldBeTrue();

        queue.Reader.TryRead(out var trigger).ShouldBeTrue();
        trigger.ShouldBe(ScanTrigger.Manual);
        queue.IsQueued.ShouldBeFalse();
        queue.Request(ScanTrigger.Scheduled).ShouldBeTrue();
    }

    [Fact]
    public void Status_keeps_the_last_report_when_a_later_scan_produces_none()
    {
        var status = new LibraryScanStatus();
        var queue = new LibraryScanQueue();
        var report = new ScanReport(ScanTrigger.Manual, 1, 1, 0, 0, 0, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

        status.Started();
        status.Snapshot(queue).IsRunning.ShouldBeTrue();
        status.Finished(report);
        status.Started();
        status.Finished(null);

        var snapshot = status.Snapshot(queue);
        snapshot.IsRunning.ShouldBeFalse();
        snapshot.LastScan.ShouldBe(report);
    }
}
