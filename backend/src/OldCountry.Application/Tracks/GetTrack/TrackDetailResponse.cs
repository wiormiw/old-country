namespace OldCountry.Application.Tracks.GetTrack;

/// <summary>All the data of one track, for its detail page.</summary>
/// <param name="Track">The track as the lists show it.</param>
/// <param name="FileName">The name of the local file (not its full path), when it has one.</param>
/// <param name="SizeBytes">The size of the local file.</param>
/// <param name="BitrateKbps">The bitrate of the local file, 0 when it is not known.</param>
/// <param name="SampleRateHz">The sample rate of the local file, 0 when it is not known.</param>
/// <param name="Playlists">The playlists that hold the track.</param>
public sealed record TrackDetailResponse(
    TrackResponse Track,
    string? FileName,
    long? SizeBytes,
    int? BitrateKbps,
    int? SampleRateHz,
    IReadOnlyList<PlaylistRef> Playlists);
