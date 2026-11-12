using OldCountry.Domain.Errors;
using OldCountry.Domain.Library;

namespace OldCountry.Domain.Tests.Library;

public sealed class ValueObjectTests
{
    [Theory]
    [InlineData("dQw4w9WgXcQ")]
    [InlineData("a-b_c-d_e-f")]
    public void YouTubeVideoId_AcceptsElevenUrlSafeCharacters(string value) =>
        YouTubeVideoId.Create(value).Value.ShouldBe(new YouTubeVideoId(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("tooShort")]
    [InlineData("twelve-chars")]
    [InlineData("has space!!")]
    public void YouTubeVideoId_RejectsAnythingElse(string? value) =>
        YouTubeVideoId.Create(value).Value.ShouldBeOfType<ValidationError>().Errors.ShouldContainKey("youTubeVideoId");

    [Fact]
    public void YouTubeVideoIds_AreEqualByValue() =>
        new YouTubeVideoId("dQw4w9WgXcQ").ShouldBe(new YouTubeVideoId("dQw4w9WgXcQ"));

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

    [Theory]
    [InlineData(AudioCodec.Flac, true)]
    [InlineData(AudioCodec.Alac, true)]
    [InlineData(AudioCodec.Opus, false)]
    [InlineData(AudioCodec.Mp3, false)]
    public void AudioQuality_KnowsWhichCodecsAreLossless(AudioCodec codec, bool lossless) =>
        new AudioQuality(codec, 0, 0).IsLossless.ShouldBe(lossless);

    private static readonly AudioQuality Lossless = new(AudioCodec.Flac, 1_000, 44_100);
}
