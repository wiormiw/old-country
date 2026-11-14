using OldCountry.Domain.Artists;
using OldCountry.Domain.Common;
using OldCountry.Domain.Downloads;
using OldCountry.Domain.Errors;
using OldCountry.Domain.YouTube;

namespace OldCountry.Domain.Tracks;

/// <summary>
/// One recording in the library. It has a file on the disk, a YouTube Music video, or both.
/// It always has at least one of them. A downloaded YouTube track keeps its video id.
/// </summary>
public sealed class Track : AuditableEntity<TrackId>
{
    public const int TitleMaxLength = 300;

    private Track() { }

    // These values do not change after creation, so they are init-only.
    // EF Core still sets them through the backing field.
    // LocalFile and YouTubeVideoId can change, so they have private setters.
    public string Title { get; private init; } = null!;

    public ArtistId ArtistId { get; private init; }

    public TimeSpan Duration { get; private init; }

    public LocalFile? LocalFile { get; private set; }

    public YouTubeVideoId? YouTubeVideoId { get; private set; }

    public bool IsAvailableOffline => LocalFile is not null;

    /// <summary>The reason that the last download failed. It stays until a download succeeds.</summary>
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

    /// <summary>Adds the downloaded file of a YouTube track. The track can then play without a connection.</summary>
    public void AttachLocalFile(LocalFile file)
    {
        LocalFile = file;
        DownloadProblem = null;
    }

    /// <summary>Keeps the reason for a refused download, so that the apps can show it and the user can try again.</summary>
    public void RecordDownloadProblem(DownloadProblem problem) => DownloadProblem = problem;

    /// <summary>Links a local file to a YouTube Music video, for metadata and artwork.</summary>
    public void LinkYouTube(YouTubeVideoId videoId) => YouTubeVideoId = videoId;
}
