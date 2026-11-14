using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Primitives;

namespace OldCountry.Api.Realtime;

/// <summary>
/// <c>/hubs/library</c>: the apps connect here to get the changes that occur anywhere
/// (another device, the YouTube sync, the library scan, the download worker).
/// The hub only sends to the apps. The apps make changes through the HTTP API, so the hub has no methods.
/// </summary>
internal sealed partial class LibraryHub(ILogger<LibraryHub> logger) : Hub<ILibraryClient>
{
    public const string Path = "/hubs/library";

    public override Task OnConnectedAsync()
    {
        LogConnected(Context.ConnectionId, Context.GetHttpContext()?.Request.Headers.UserAgent ?? default);
        return Task.CompletedTask;
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        if (exception is null) LogDisconnected(Context.ConnectionId);
        else LogDropped(exception, Context.ConnectionId);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "App connected for changes: {ConnectionId} ({UserAgent})")]
    private partial void LogConnected(string connectionId, StringValues userAgent);

    [LoggerMessage(Level = LogLevel.Information, Message = "App disconnected: {ConnectionId}")]
    private partial void LogDisconnected(string connectionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "App connection dropped: {ConnectionId}")]
    private partial void LogDropped(Exception exception, string connectionId);
}
