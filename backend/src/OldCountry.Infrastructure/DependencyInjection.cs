using OldCountry.Application.Abstractions.Audio;
using OldCountry.Application.Abstractions.YouTube;
using OldCountry.Infrastructure.Audio;
using OldCountry.Infrastructure.Background;
using OldCountry.Infrastructure.Persistence;
using OldCountry.Infrastructure.YouTube;

namespace OldCountry.Infrastructure;

/// <summary>Registers the adapters of the outside world and the background loops.</summary>
public static class DependencyInjection
{
    public static IHostApplicationBuilder AddInfrastructure(this IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton(TimeProvider.System);
        builder.AddPersistence();

        builder.Services.AddSingleton<IYtDlp, YtDlpProcess>();
        builder.Services.AddSingleton<IAudioMetadataReader, TagLibMetadataReader>();

        // The loops only wait and repeat. The work of each round is a use case in Application.
        builder.Services.AddHostedService<LibraryScanner>();
        builder.Services.AddHostedService<DownloadWorker>();
        builder.Services.AddHostedService<PlaylistSyncScheduler>();
        return builder;
    }
}
