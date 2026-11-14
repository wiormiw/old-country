using OldCountry.Domain.Tracks;

namespace OldCountry.Application.Abstractions.Audio;

/// <summary>The tags and stream properties of an audio file that a scan needs.</summary>
public sealed record AudioMetadata(string? Title, string? Artist, TimeSpan Duration, AudioCodec Codec, int BitrateKbps, int SampleRateHz);
