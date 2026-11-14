namespace OldCountry.Domain.Tracks;

/// <summary>How a file is encoded. The database keeps it in the row of its track, as a complex type.</summary>
public sealed record AudioQuality(AudioCodec Codec, int BitrateKbps, int SampleRateHz)
{
    public bool IsLossless => Codec is AudioCodec.Flac or AudioCodec.Alac or AudioCodec.Wav;
}
