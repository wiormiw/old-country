using OldCountry.Application.Announcements;
using OldCountry.Application.Common.Library;
using OldCountry.Application.Common.YouTube;
using OldCountry.Application.Downloads;
using OldCountry.Application.Events;
using OldCountry.Application.Pipeline.Validation;
using OldCountry.Application.Playlists;
using OldCountry.Application.Realtime;
using OldCountry.Application.Tracks;
using OldCountry.Application.YouTubeMusic;
namespace OldCountry.Application;

/// <summary>Registers all use cases. Each feature has one line here. There is no assembly scan.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddDomainEvents();
        // The queue of change notices between the database and the SignalR hub.
        services.AddSingleton<ChangeFeed>();

        // Shared by the features that add tracks.
        services.AddScoped<ArtistDirectory>();

        // Shared by the features that talk to YouTube.
        services.AddOptions<YouTubeMusicOptions>()
            .BindConfiguration(YouTubeMusicOptions.Section)
            .ValidateWithValidator<YouTubeMusicOptions, YouTubeMusicOptionsValidator>()
            .ValidateOnStart();
        services.AddSingleton<YouTubeRateLimit>();

        TracksFeature.AddServices(services);
        PlaylistsFeature.AddServices(services);
        DownloadsFeature.AddServices(services);
        YouTubeMusicFeature.AddServices(services);
        AnnouncementsFeature.AddServices(services);
        return services;
    }
}
