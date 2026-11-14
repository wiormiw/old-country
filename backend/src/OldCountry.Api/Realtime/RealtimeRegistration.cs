using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Connections;

namespace OldCountry.Api.Realtime;

/// <summary>Live change notices: the hub, its settings, and the broadcaster that sends to it.</summary>
internal static class RealtimeRegistration
{
    public static IServiceCollection AddRealtime(this IServiceCollection services, IHostEnvironment environment)
    {
        // The broadcaster reads the change feed (Application) and tells the connected apps.
        services.AddHostedService<ChangeBroadcaster>();

        services.AddSignalR(hub =>
            {
                // The server sends a ping each 15 seconds. An app that is silent for 30 seconds is gone.
                // The clients use the same values: a ping each 15 seconds, and a stop after 30 seconds.
                hub.KeepAliveInterval = TimeSpan.FromSeconds(15);
                hub.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
                hub.HandshakeTimeout = TimeSpan.FromSeconds(10);
                // The apps only listen. The largest message from an app is the handshake or a ping.
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
            // WebSockets first. Server-Sent Events and long polling are for networks that block WebSockets.
            hub.Transports = HttpTransportType.WebSockets | HttpTransportType.ServerSentEvents | HttpTransportType.LongPolling;
            // A client that disconnects for a short time (a Wi-Fi problem, a phone that wakes up) gets
            // the messages that it did not receive, if it asks for them. The .NET and JS clients ask.
            hub.AllowStatefulReconnects = true;
        });
        return app;
    }
}
