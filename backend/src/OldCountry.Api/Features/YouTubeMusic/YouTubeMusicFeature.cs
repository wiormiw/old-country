using OldCountry.Api.Common.Messaging;
using OldCountry.Api.Common.Validation;
using OldCountry.Api.Features.YouTubeMusic.DownloadTracks;
using OldCountry.Api.Features.YouTubeMusic.ImportPlaylist;
using OldCountry.Api.Features.YouTubeMusic.SearchYouTubeMusic;
using OldCountry.Api.Features.YouTubeMusic.StreamYouTubeAudio;
using OldCountry.Api.Features.YouTubeMusic.SyncPlaylists;

namespace OldCountry.Api.Features.YouTubeMusic;

/// <summary>YouTube Music, through yt-dlp: search, stream, download for offline, import playlists.</summary>
internal static class YouTubeMusicFeature
{
    public static void AddServices(IServiceCollection services)
    {
        services.AddOptions<YouTubeMusicOptions>()
            .BindConfiguration(YouTubeMusicOptions.Section)
            .ValidateWithValidator<YouTubeMusicOptions, YouTubeMusicOptionsValidator>()
            .ValidateOnStart();
        services.AddSingleton<IYtDlp, YtDlpProcess>();

        // Streaming: yt-dlp fetches YouTube audio into a size-capped cache that is served as files.
        services.AddSingleton<YouTubeAudioCache>();

        // Downloads: jobs stored in Postgres, taken one at a time (under a lease) by one background
        // worker; a Channel-based doorbell wakes it when jobs are queued.
        services.AddSingleton<DownloadDoorbell>();
        services.AddSingleton<DownloadPauses>();
        services.AddHostedService<DownloadWorker>();

        // Playlists imported from YouTube Music sync with it on their own.
        services.AddSingleton<PlaylistSyncScheduler>();
        services.AddHostedService(provider => provider.GetRequiredService<PlaylistSyncScheduler>());

        services.AddValidator<SearchYouTubeMusicRequest, SearchYouTubeMusicRequestValidator>();
        services.AddValidator<DownloadTracksRequest, DownloadTracksRequestValidator>();
        services.AddValidator<ImportPlaylistRequest, ImportPlaylistRequestValidator>();

        services.AddQuery<SearchYouTubeMusicQuery, SearchYouTubeMusicResult, SearchYouTubeMusicHandler>();
        services.AddCommand<DownloadTrackCommand, DownloadTrackResult, DownloadTrackHandler>();
        services.AddCommand<QueueDownloadsCommand, QueueDownloadsResult, QueueDownloadsHandler>();
        services.AddCommand<QueuePlaylistDownloadsCommand, QueuePlaylistDownloadsResult, QueuePlaylistDownloadsHandler>();
        services.AddQuery<ListDownloadsQuery, DownloadsResponse, ListDownloadsHandler>();
        services.AddCommand<SyncPlaylistCommand, SyncPlaylistResult, SyncPlaylistHandler>();
        services.AddCommand<ImportPlaylistCommand, ImportPlaylistResult, ImportPlaylistHandler>();
    }

    public static void MapEndpoints(IEndpointRouteBuilder api)
    {
        var youTube = api.MapGroup("/youtube-music").WithTags("YouTube Music");
        SearchYouTubeMusicEndpoint.Map(youTube);
        StreamYouTubeAudioEndpoint.Map(youTube);
        DownloadTracksEndpoints.Map(youTube);
        ImportPlaylistEndpoint.Map(youTube);
        SyncPlaylistEndpoint.Map(youTube);
    }
}
