using OldCountry.Api.Common.Messaging;
using OldCountry.Api.Common.Validation;
using OldCountry.Api.Features.YouTubeMusic.DownloadTracks;
using OldCountry.Api.Features.YouTubeMusic.ImportPlaylist;
using OldCountry.Api.Features.YouTubeMusic.SearchYouTubeMusic;
using OldCountry.Api.Features.YouTubeMusic.StreamYouTubeAudio;

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

        // Downloads: an in-memory queue drained by one background worker.
        services.AddSingleton<DownloadQueue>();
        services.AddHostedService<DownloadWorker>();

        services.AddValidator<SearchYouTubeMusicRequest, SearchYouTubeMusicRequestValidator>();
        services.AddValidator<DownloadTracksRequest, DownloadTracksRequestValidator>();
        services.AddValidator<ImportPlaylistRequest, ImportPlaylistRequestValidator>();

        services.AddQuery<SearchYouTubeMusicQuery, SearchYouTubeMusicResult, SearchYouTubeMusicHandler>();
        services.AddCommand<DownloadTrackCommand, DownloadTrackResult, DownloadTrackHandler>();
        services.AddCommand<ImportPlaylistCommand, ImportPlaylistResult, ImportPlaylistHandler>();
    }

    public static void MapEndpoints(IEndpointRouteBuilder api)
    {
        var youTube = api.MapGroup("/youtube-music").WithTags("YouTube Music");
        SearchYouTubeMusicEndpoint.Map(youTube);
        StreamYouTubeAudioEndpoint.Map(youTube);
        DownloadTracksEndpoints.Map(youTube);
        ImportPlaylistEndpoint.Map(youTube);
    }
}
