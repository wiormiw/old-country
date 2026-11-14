using OldCountry.Domain.Tracks;

namespace OldCountry.Domain.Tests.Tracks;

public sealed class AudioQualityTests
{
    [Theory]
    [InlineData(AudioCodec.Flac, true)]
    [InlineData(AudioCodec.Alac, true)]
    [InlineData(AudioCodec.Opus, false)]
    [InlineData(AudioCodec.Mp3, false)]
    public void AudioQuality_KnowsWhichCodecsAreLossless(AudioCodec codec, bool lossless) =>
        new AudioQuality(codec, 0, 0).IsLossless.ShouldBe(lossless);
}
