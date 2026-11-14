using OldCountry.Api.Endpoints.Downloads;
using OldCountry.Api.Endpoints.Playlists;
using OldCountry.Api.Endpoints.Tracks;
using OldCountry.Api.Endpoints.YouTubeMusic;

namespace OldCountry.Api.Endpoints;

/// <summary>All HTTP endpoints under <c>/api</c>, in a list that you write by hand. A new endpoint needs one line here.</summary>
internal static class ApiEndpoints
{
    public static IEndpointRouteBuilder MapApiEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        var tracks = api.MapGroup("/tracks").WithTags("Tracks");
        AddTrackEndpoint.Map(tracks);
        GetTrackEndpoint.Map(tracks);
        ListTracksEndpoint.Map(tracks);
        ScanLibraryEndpoints.Map(tracks);
        StreamTrackEndpoint.Map(tracks);

        var playlists = api.MapGroup("/playlists").WithTags("Playlists");
        CreatePlaylistEndpoint.Map(playlists);
        ListPlaylistsEndpoint.Map(playlists);
        GetPlaylistEndpoint.Map(playlists);
        UpdatePlaylistEndpoint.Map(playlists);
        DeletePlaylistEndpoint.Map(playlists);
        AddPlaylistTracksEndpoint.Map(playlists);
        RemovePlaylistEntryEndpoint.Map(playlists);
        MovePlaylistEntryEndpoint.Map(playlists);

        // The download routes stay under /youtube-music, so the URLs of the apps do not change.
        var youTube = api.MapGroup("/youtube-music").WithTags("YouTube Music");
        SearchYouTubeMusicEndpoint.Map(youTube);
        StreamYouTubeAudioEndpoint.Map(youTube);
        DownloadTracksEndpoints.Map(youTube);
        ImportPlaylistEndpoint.Map(youTube);
        SyncPlaylistEndpoint.Map(youTube);
        return app;
    }
}
