namespace OldCountry.Application.Abstractions.YouTube;

/// <summary>A finished download: the location of the file and the video that it came from.</summary>
public sealed record YtDownload(string Path, YtVideo Video, int BitrateKbps, int SampleRateHz);
