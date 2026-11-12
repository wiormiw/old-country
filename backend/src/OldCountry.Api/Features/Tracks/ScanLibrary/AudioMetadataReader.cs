using OldCountry.Api.Common.Library;
using OldCountry.Domain.Library;

namespace OldCountry.Api.Features.Tracks.ScanLibrary;

/// <summary>What a scan needs from an audio file's tags and stream properties.</summary>
internal sealed record AudioMetadata(string? Title, string? Artist, TimeSpan Duration, AudioCodec Codec, int BitrateKbps, int SampleRateHz);

/// <summary>Reads tags from a file. An interface so scans can be tested without real audio.</summary>
internal interface IAudioMetadataReader
{
    /// <summary>The file's metadata, or <c>null</c> when it can't be read as audio.</summary>
    AudioMetadata? Read(string path);
}

/// <summary>TagLib# implementation: ID3, Vorbis comments, MP4 atoms and RIFF, behind one API.</summary>
internal sealed class TagLibMetadataReader : IAudioMetadataReader
{
    public AudioMetadata? Read(string path)
    {
        if (!AudioFormats.TryGetCodec(path, out var codec)) return null;
        try
        {
            using var file = TagLib.File.Create(path);
            var properties = file.Properties;
            if (properties is null || properties.Duration <= TimeSpan.Zero) return null;

            // .m4a holds either AAC or Apple Lossless; only the stream description tells them apart.
            if (codec == AudioCodec.Aac && properties.Description?.Contains("alac", StringComparison.OrdinalIgnoreCase) == true)
                codec = AudioCodec.Alac;

            return new AudioMetadata(
                Blank(file.Tag.Title),
                Blank(file.Tag.FirstPerformer) ?? Blank(file.Tag.FirstAlbumArtist),
                properties.Duration,
                codec,
                properties.AudioBitrate,
                properties.AudioSampleRate);
        }
        catch (Exception exception) when (exception is TagLib.CorruptFileException or TagLib.UnsupportedFormatException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
