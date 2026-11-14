using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Domain.YouTube;

namespace OldCountry.Application.Downloads.DownloadTrack;

/// <summary>Downloads one video and keeps it as a track that can play without a connection. The download worker runs it.</summary>
[NoTransaction]
public sealed record DownloadTrackCommand(YouTubeVideoId VideoId) : ICommand<DownloadTrackResult>;
