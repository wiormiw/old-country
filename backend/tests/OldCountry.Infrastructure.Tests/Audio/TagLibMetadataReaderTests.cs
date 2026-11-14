using OldCountry.Domain.Tracks;
using OldCountry.Infrastructure.Audio;
using OldCountry.Tests.Shared;

namespace OldCountry.Infrastructure.Tests.Audio;

/// <summary>Reads tags and stream properties from real files.</summary>
public sealed class TagLibMetadataReaderTests : IDisposable
{
    private readonly string _directory = TestAudio.NewTempDirectory();

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Reads_duration_and_stream_properties_from_a_real_wav()
    {
        var path = TestAudio.WriteSilentWav(_directory, "tone.wav", seconds: 2, sampleRate: 8_000);

        var metadata = new TagLibMetadataReader().Read(path).ShouldNotBeNull();

        metadata.Duration.TotalSeconds.ShouldBe(2, tolerance: 0.05);
        metadata.SampleRateHz.ShouldBe(8_000);
        metadata.Codec.ShouldBe(AudioCodec.Wav);
        metadata.Title.ShouldBeNull();
        metadata.Artist.ShouldBeNull();
    }

    [Fact]
    public void A_file_that_is_not_really_audio_reads_as_null()
    {
        var path = Path.Combine(_directory, "fake.flac");
        File.WriteAllText(path, "definitely not a flac stream");

        new TagLibMetadataReader().Read(path).ShouldBeNull();
    }
}
