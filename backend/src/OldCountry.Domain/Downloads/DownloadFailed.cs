using OldCountry.Domain.Common;
using OldCountry.Domain.YouTube;

namespace OldCountry.Domain.Downloads;

/// <summary>A YouTube download stopped without a track, because YouTube refused it or it failed.</summary>
/// <param name="Job">The download job that stopped.</param>
/// <param name="Video">The YouTube video of the job.</param>
/// <param name="Code">The reason as a stable code, for example <c>youtube.premium_only</c> or <c>download.gave_up</c>.</param>
/// <param name="Message">The reason in words.</param>
public sealed record DownloadFailed(DownloadJobId Job, YouTubeVideoId Video, string Code, string Message) : IDomainEvent;
