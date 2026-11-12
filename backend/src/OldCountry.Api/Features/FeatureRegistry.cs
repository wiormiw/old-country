using OldCountry.Api.Common.Library;
using OldCountry.Api.Features.Playlists;
using OldCountry.Api.Features.Tracks;
using OldCountry.Api.Features.YouTubeMusic;

namespace OldCountry.Api.Features;

/// <summary>Every feature in the API, listed explicitly. Adding a feature means adding a line here.</summary>
internal static class FeatureRegistry
{
    public static IServiceCollection AddFeatures(this IServiceCollection services)
    {
        // Shared library helpers (Common/Library), used by every feature that adds tracks.
        services.AddScoped<ArtistDirectory>();

        TracksFeature.AddServices(services);
        PlaylistsFeature.AddServices(services);
        YouTubeMusicFeature.AddServices(services);
        return services;
    }

    public static IEndpointRouteBuilder MapFeatures(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");
        TracksFeature.MapEndpoints(api);
        PlaylistsFeature.MapEndpoints(api);
        YouTubeMusicFeature.MapEndpoints(api);
        return app;
    }
}
