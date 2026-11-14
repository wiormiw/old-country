using OldCountry.Domain.Common;
using OldCountry.Domain.Tracks;
using OldCountry.Domain.YouTube;

namespace OldCountry.Domain.Downloads;

/// <summary>A YouTube download finished. The track is now a file in the library.</summary>
public sealed record DownloadFinished(DownloadJobId Job, YouTubeVideoId Video, TrackId Track) : IDomainEvent;
