using OldCountry.Application.Common.Library;
using OldCountry.Domain.Errors;

namespace OldCountry.Application.Common.YouTube;

/// <summary>The <c>YouTubeMusic</c> settings section.</summary>
public sealed class YouTubeMusicOptions
{
    public const string Section = "YouTubeMusic";
    public const string DefaultFolderName = "YouTube Music";

    /// <summary>The yt-dlp executable: a name on PATH or an absolute path.</summary>
    public string YtDlpPath { get; init; } = "yt-dlp";

    /// <summary>
    /// The JavaScript runtime that yt-dlp uses for the YouTube challenges: node, deno, quickjs or bun.
    /// You can add its location ("node:/usr/bin/node"). The default is Node, because the development of Deno stops in 2027.
    /// An empty value lets yt-dlp choose (it enables only Deno).
    /// </summary>
    public string JsRuntime { get; init; } = "node";

    /// <summary>
    /// A cookies.txt file (Netscape format) from a browser that is signed in to YouTube Music.
    /// Private playlists and liked songs need it. Public search, playback and downloads work without it.
    /// </summary>
    public string? CookiesFile { get; init; }

    /// <summary>The folder for downloads. The default is "YouTube Music" in the first library folder.</summary>
    public string? DownloadFolder { get; init; }

    /// <summary>The audio format of downloads: opus (smallest, the YouTube format), m4a, mp3 or flac.</summary>
    public string AudioFormat { get; init; } = "opus";

    /// <summary>The cache folder for the audio of playback. The default is a folder in the system temp folder.</summary>
    public string? StreamCacheFolder { get; init; }

    /// <summary>The maximum size of the playback cache. The files that played least recently go first.</summary>
    public int StreamCacheMaxMegabytes { get; init; } = 2048;

    /// <summary>The maximum time of one yt-dlp run. After this time the server stops it.</summary>
    public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>The wait between two downloads, so that a long queue looks less like a bot.</summary>
    public TimeSpan DownloadPause { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The rest of the download queue when YouTube asks the server to slow down (HTTP 429, "confirm you're not a bot").
    /// Each new rate limit in a row doubles the rest, to a maximum of 8 times this value.
    /// </summary>
    public TimeSpan RateLimitPause { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// The interval of the automatic sync of playlists that came from YouTube Music. Thus tracks added there also come here.
    /// A playlist syncs when its last sync is older than this value.
    /// <c>00:00:00</c> stops the automatic sync. The Sync button continues to work.
    /// </summary>
    public TimeSpan PlaylistSyncInterval { get; init; } = TimeSpan.FromHours(6);

    /// <summary>
    /// The time that a worker owns a download that it started. It is long enough for the slowest download
    /// (two yt-dlp runs and the save). If the server stops during a download, the job runs again after this time.
    /// </summary>
    public TimeSpan DownloadLease => CommandTimeout * 2 + TimeSpan.FromMinutes(2);

    public static readonly IReadOnlySet<string> AudioFormats = new HashSet<string>(StringComparer.Ordinal) { "opus", "m4a", "mp3", "flac" };

    /// <summary>
    /// The folder for downloads, or the reason that there is none.
    /// It must be in a library folder, so the server can play the downloads and the library scan finds them.
    /// </summary>
    public Validated<string> ResolveDownloadFolder(LibraryOptions library)
    {
        if (DownloadFolder is null && library.Roots.Count == 0)
            return ValidationError.For("youTubeMusic.downloadFolder",
                "Downloads need a library folder (Library:Roots) or YouTubeMusic:DownloadFolder inside one.");

        var folder = DownloadFolder ?? Path.Combine(library.Roots[0], DefaultFolderName);
        if (!LibraryPaths.IsInsideLibrary(Path.Combine(folder, "probe"), library.Roots))
            return ValidationError.For("youTubeMusic.downloadFolder",
                $"The download folder '{folder}' must be inside one of the library folders (Library:Roots).");
        return folder;
    }
}
