using OldCountry.Application.Abstractions.Audio;
using OldCountry.Application.Common.Library;
using OldCountry.Domain.Tracks;

namespace OldCountry.Infrastructure.Audio;

/// <summary>The TagLib# reader: ID3, Vorbis comments, MP4 atoms and RIFF, through one API.</summary>
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

            // An .m4a file holds AAC or Apple Lossless. Only the stream description shows which.
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
