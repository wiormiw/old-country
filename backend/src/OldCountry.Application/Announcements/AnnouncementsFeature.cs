using OldCountry.Application.Events;
using OldCountry.Domain.Downloads;
using OldCountry.Domain.Playlists;
namespace OldCountry.Application.Announcements;

/// <summary>Domain events that become news for the apps ("Downloaded “Song”").</summary>
internal static class AnnouncementsFeature
{
    public static void AddServices(IServiceCollection services)
    {
        services.AddDomainEventHandler<DownloadFinished, AnnounceDownloadFinished>();
        services.AddDomainEventHandler<DownloadFailed, AnnounceDownloadFailed>();
        services.AddDomainEventHandler<SongsAddedFromYouTube, AnnounceSongsFromYouTube>();
    }
}
