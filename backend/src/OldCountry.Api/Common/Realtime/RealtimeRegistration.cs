using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Connections;
using OldCountry.Api.Common.Events;
using OldCountry.Domain.Library;

namespace OldCountry.Api.Common.Realtime;

/// <summary>Live change notifications: the hub, its settings, and what feeds it.</summary>
internal static class RealtimeRegistration
{
    public static IServiceCollection AddRealtime(this IServiceCollection services, IHostEnvironment environment)
    {
        services.AddSingleton<ChangeFeed>();
        services.AddHostedService<ChangeBroadcaster>();

        // Domain events that become news for the apps.
        services.AddDomainEventHandler<DownloadFinished, AnnounceDownloadFinished>();
        services.AddDomainEventHandler<DownloadFailed, AnnounceDownloadFailed>();
        services.AddDomainEventHandler<SongsAddedFromYouTube, AnnounceSongsFromYouTube>();

        services.AddSignalR(hub =>
            {
                // The server pings every 15 s; an app silent for 30 s (no pings back) is gone.
                // Clients use the matching defaults: ping every 15 s, give up after 30 s.
                hub.KeepAliveInterval = TimeSpan.FromSeconds(15);
                hub.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
                hub.HandshakeTimeout = TimeSpan.FromSeconds(10);
                // Apps only listen; the largest thing they send is the handshake or a ping.
                hub.MaximumReceiveMessageSize = 4 * 1024;
                hub.MaximumParallelInvocationsPerClient = 1;
                hub.EnableDetailedErrors = environment.IsDevelopment();
            })
            .AddJsonProtocol(json => json.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        return services;
    }

    public static IEndpointRouteBuilder MapRealtime(this IEndpointRouteBuilder app)
    {
        app.MapHub<LibraryHub>(LibraryHub.Path, hub =>
        {
            // WebSockets first; Server-Sent Events and long polling for networks that block them.
            hub.Transports = HttpTransportType.WebSockets | HttpTransportType.ServerSentEvents | HttpTransportType.LongPolling;
            // A client that drops briefly (Wi-Fi hiccup, phone waking up) gets the messages it
            // missed instead of a new connection, when it asks for it (the .NET and JS clients do).
            hub.AllowStatefulReconnects = true;
        });
        return app;
    }
}
