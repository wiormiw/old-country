using OldCountry.Application.Pipeline;
using OldCountry.Application.Pipeline.Validation;
using OldCountry.Application.YouTubeMusic.ImportPlaylist;
using OldCountry.Application.YouTubeMusic.SearchYouTubeMusic;
using OldCountry.Application.YouTubeMusic.StreamYouTubeAudio;
using OldCountry.Application.YouTubeMusic.SyncDuePlaylists;
using OldCountry.Application.YouTubeMusic.SyncPlaylist;
namespace OldCountry.Application.YouTubeMusic;

/// <summary>YouTube Music through yt-dlp: search, play, import playlists, and keep them in sync.</summary>
internal static class YouTubeMusicFeature
{
    public static void AddServices(IServiceCollection services)
    {
        // Playback: yt-dlp puts the YouTube audio in a cache with a size limit. The API serves the files.
        services.AddSingleton<YouTubeAudioCache>();

        // Imported playlists sync with YouTube Music automatically.
        services.AddSingleton<PlaylistSyncState>();

        services.AddValidator<SearchYouTubeMusicRequest, SearchYouTubeMusicRequestValidator>();
        services.AddValidator<ImportPlaylistRequest, ImportPlaylistRequestValidator>();

        services.AddQuery<SearchYouTubeMusicQuery, SearchYouTubeMusicResult, SearchYouTubeMusicHandler>();
        services.AddCommand<ImportPlaylistCommand, ImportPlaylistResult, ImportPlaylistHandler>();
        services.AddCommand<SyncPlaylistCommand, SyncPlaylistResult, SyncPlaylistHandler>();
        services.AddCommand<SyncDuePlaylistsCommand, int, SyncDuePlaylistsHandler>();
    }
}
