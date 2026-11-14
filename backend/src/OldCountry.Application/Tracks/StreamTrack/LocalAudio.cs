namespace OldCountry.Application.Tracks.StreamTrack;

/// <summary>A file on the disk that the player can read, and its content type.</summary>
public sealed record LocalAudio(string Path, string ContentType);
