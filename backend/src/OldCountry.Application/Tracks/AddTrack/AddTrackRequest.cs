using OldCountry.Domain.Tracks;

namespace OldCountry.Application.Tracks.AddTrack;

/// <summary>Adds a track to the library: a local file, a YouTube Music video, or both.</summary>
public sealed record AddTrackRequest
{
    /// <summary>The title of the track, 1-300 characters.</summary>
    public required string Title { get; init; }

    /// <summary>The display name of the artist. An existing artist with the same name (any case) is used again.</summary>
    public required string Artist { get; init; }

    /// <summary>The length in seconds, at most 24 hours.</summary>
    public required int DurationSeconds { get; init; }

    /// <summary>The absolute path of the audio file on the server, if the server has the file.</summary>
    public string? LocalPath { get; init; }

    /// <summary>The size of the local file in bytes. It is necessary with <see cref="LocalPath"/>.</summary>
    public long? SizeBytes { get; init; }

    /// <summary>The codec of the local file.</summary>
    public AudioCodec Codec { get; init; } = AudioCodec.Unknown;

    /// <summary>The bitrate of the local file in kbit/s, 0 when it is not known.</summary>
    public int BitrateKbps { get; init; }

    /// <summary>The sample rate of the local file in Hz, 0 when it is not known.</summary>
    public int SampleRateHz { get; init; }

    /// <summary>The 11-character YouTube Music video id, if the track is linked.</summary>
    public string? YouTubeVideoId { get; init; }
}
