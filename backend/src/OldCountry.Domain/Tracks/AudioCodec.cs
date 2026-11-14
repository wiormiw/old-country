namespace OldCountry.Domain.Tracks;

/// <summary>The codec of an audio file. The database keeps the name, so do not rename the values.</summary>
public enum AudioCodec
{
    Unknown = 0,
    Flac = 1,
    Mp3 = 2,
    Aac = 3,
    Opus = 4,
    Vorbis = 5,
    Alac = 6,
    Wav = 7,
}
