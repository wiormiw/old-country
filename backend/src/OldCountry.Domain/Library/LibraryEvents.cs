using OldCountry.Domain.Common;

namespace OldCountry.Domain.Library;

/// <summary>A YouTube download finished: the song is now a file in the library.</summary>
public sealed record DownloadFinished(DownloadJobId Job, YouTubeVideoId Video, TrackId Track) : IDomainEvent;

/// <summary>A YouTube download ended without a song: refused by YouTube, or failed.</summary>
/// <param name="Job">The download job that ended.</param>
/// <param name="Video">The YouTube video it was for.</param>
/// <param name="Code">Why, as a stable code: <c>youtube.premium_only</c>, <c>download.gave_up</c>…</param>
/// <param name="Message">Why, in words.</param>
public sealed record DownloadFailed(DownloadJobId Job, YouTubeVideoId Video, string Code, string Message) : IDomainEvent;

/// <summary>Songs from a YouTube Music playlist were added to its playlist here (an import or a sync).</summary>
public sealed record SongsAddedFromYouTube(PlaylistId Playlist, int Count) : IDomainEvent;
