namespace OldCountry.Application.Abstractions.Audio;

/// <summary>Reads the tags of a file. It is an interface, so tests of the scan do not need real audio.</summary>
public interface IAudioMetadataReader
{
    /// <summary>The metadata of the file, or <c>null</c> when the file is not readable audio.</summary>
    AudioMetadata? Read(string path);
}
