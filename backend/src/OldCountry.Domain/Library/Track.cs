using OldCountry.Domain.Common;
using OldCountry.Domain.Errors;

namespace OldCountry.Domain.Library;

/// <summary>
/// One recording in the library. It can live on disk, on YouTube Music, or both (a YouTube track
/// downloaded for offline keeps its video id), but never neither.
/// </summary>
public sealed class Track : AuditableEntity<TrackId>
{
    public const int TitleMaxLength = 300;

    private Track() { }

    // Fixed at creation: init-only (EF still materializes them through the backing field).
    // LocalFile and YouTubeVideoId change over a track's life, so they keep private setters.
    public string Title { get; private init; } = null!;

    public ArtistId ArtistId { get; private init; }

    public TimeSpan Duration { get; private init; }

    public LocalFile? LocalFile { get; private set; }

    public YouTubeVideoId? YouTubeVideoId { get; private set; }

    public bool IsAvailableOffline => LocalFile is not null;

    /// <summary>Why the last download attempt failed, if it did and none has worked since.</summary>
    public DownloadProblem? DownloadProblem { get; private set; }

    public static Validated<Track> Create(string title, ArtistId artistId, TimeSpan duration, LocalFile? localFile, YouTubeVideoId? youTubeVideoId)
    {
        var trimmed = title.Trim();
        if (trimmed.Length is 0 or > TitleMaxLength)
            return ValidationError.For("title", $"Must be 1-{TitleMaxLength} characters.");
        if (duration <= TimeSpan.Zero)
            return ValidationError.For("duration", "Must be greater than zero.");
        if (localFile is null && youTubeVideoId is null)
            return ValidationError.For("source", "A track needs a local file, a YouTube video id, or both.");

        return new Track
        {
            Id = TrackId.New(),
            Title = trimmed,
            ArtistId = artistId,
            Duration = duration,
            LocalFile = localFile,
            YouTubeVideoId = youTubeVideoId,
        };
    }

    /// <summary>A YouTube track was downloaded: it is now playable offline too.</summary>
    public void AttachLocalFile(LocalFile file)
    {
        LocalFile = file;
        DownloadProblem = null;
    }

    /// <summary>A download was refused; remembered so it can be shown, and retried later on purpose.</summary>
    public void RecordDownloadProblem(DownloadProblem problem) => DownloadProblem = problem;

    /// <summary>A local file was matched to YouTube Music, for metadata and artwork.</summary>
    public void LinkYouTube(YouTubeVideoId videoId) => YouTubeVideoId = videoId;
}
