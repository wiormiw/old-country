using OldCountry.Domain.YouTube;

namespace OldCountry.Application.Abstractions.YouTube;

/// <summary>
/// All requests of old-country to YouTube, through the yt-dlp program.
/// It is an interface, so the tests of the use cases do not need the network or the program.
/// </summary>
public interface IYtDlp
{
    Task<IReadOnlyList<YtVideo>> SearchAsync(string query, int limit, CancellationToken cancellationToken);

    /// <summary>Gets the best audio stream without conversion into <c>{folder}/{id}.{ext}</c>. Returns the path.</summary>
    Task<string> FetchAudioAsync(YouTubeVideoId videoId, string folder, CancellationToken cancellationToken);

    Task<YtPlaylist> GetPlaylistAsync(Uri playlistUrl, CancellationToken cancellationToken);

    Task<YtDownload> DownloadAudioAsync(YouTubeVideoId videoId, string folder, CancellationToken cancellationToken);
}
