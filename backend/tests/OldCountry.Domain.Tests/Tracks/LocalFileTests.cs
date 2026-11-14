using OldCountry.Domain.Errors;
using OldCountry.Domain.Tracks;

namespace OldCountry.Domain.Tests.Tracks;

public sealed class LocalFileTests
{
    [Fact]
    public void LocalFile_RequiresAnAbsolutePath() =>
        LocalFile.Create("music/song.flac", 1_000, Lossless).Value
            .ShouldBeOfType<ValidationError>().Errors.ShouldContainKey("localPath");

    [Fact]
    public void LocalFile_RequiresAPositiveSize() =>
        LocalFile.Create("/srv/music/song.flac", 0, Lossless).Value
            .ShouldBeOfType<ValidationError>().Errors.ShouldContainKey("sizeBytes");

    [Fact]
    public void LocalFiles_AreEqualByValue_IncludingTheirNestedQuality() =>
        new LocalFile("/srv/music/a.flac", 10, Lossless).ShouldBe(new LocalFile("/srv/music/a.flac", 10, Lossless with { }));

    private static readonly AudioQuality Lossless = new(AudioCodec.Flac, 1_000, 44_100);
}
