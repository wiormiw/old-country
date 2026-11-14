using System.Buffers.Binary;

namespace OldCountry.Tests.Shared;

/// <summary>Writes real, minimal audio files for scan tests: PCM WAV, silent, mono, 16-bit.</summary>
internal static class TestAudio
{
    public static string WriteSilentWav(string directory, string name, int seconds = 1, int sampleRate = 8_000)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);

        const short channels = 1, bitsPerSample = 16;
        var blockAlign = (short)(channels * bitsPerSample / 8);
        var dataSize = seconds * sampleRate * blockAlign;
        var bytes = new byte[44 + dataSize];
        var span = bytes.AsSpan();

        "RIFF"u8.CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], 36 + dataSize);
        "WAVE"u8.CopyTo(span[8..]);
        "fmt "u8.CopyTo(span[12..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16);           // fmt chunk size
        BinaryPrimitives.WriteInt16LittleEndian(span[20..], 1);            // PCM
        BinaryPrimitives.WriteInt16LittleEndian(span[22..], channels);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], sampleRate * blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(span[32..], blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(span[34..], bitsPerSample);
        "data"u8.CopyTo(span[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..], dataSize);     // The samples stay zero: silence.

        File.WriteAllBytes(path, bytes);
        return path;
    }

    public static string NewTempDirectory() =>
        Directory.CreateTempSubdirectory("old-country-tests-").FullName;
}
