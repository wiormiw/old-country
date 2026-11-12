using System.Collections.Frozen;
using OldCountry.Domain.Library;

namespace OldCountry.Api.Common.Library;

/// <summary>The audio formats the library understands, by file extension.</summary>
internal static class AudioFormats
{
    private sealed record Format(AudioCodec Codec, string ContentType);

    private static readonly FrozenDictionary<string, Format> ByExtension = new Dictionary<string, Format>
    {
        [".flac"] = new(AudioCodec.Flac, "audio/flac"),
        [".mp3"] = new(AudioCodec.Mp3, "audio/mpeg"),
        [".m4a"] = new(AudioCodec.Aac, "audio/mp4"),
        [".aac"] = new(AudioCodec.Aac, "audio/aac"),
        [".opus"] = new(AudioCodec.Opus, "audio/ogg"),
        [".ogg"] = new(AudioCodec.Vorbis, "audio/ogg"),
        [".oga"] = new(AudioCodec.Vorbis, "audio/ogg"),
        [".wav"] = new(AudioCodec.Wav, "audio/wav"),
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public static bool TryGetCodec(string path, out AudioCodec codec)
    {
        var found = ByExtension.TryGetValue(Path.GetExtension(path), out var format);
        codec = found ? format!.Codec : AudioCodec.Unknown;
        return found;
    }

    /// <summary>The MIME type to serve a file with; a generic binary type for anything unknown.</summary>
    public static string ContentTypeOf(string path) =>
        ByExtension.TryGetValue(Path.GetExtension(path), out var format) ? format.ContentType : "application/octet-stream";
}
