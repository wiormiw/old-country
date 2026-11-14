using Microsoft.AspNetCore.SignalR;
using OldCountry.Application.Realtime;

namespace OldCountry.Api.Realtime;

/// <summary>
/// Sends committed changes to all connected apps.
/// Changes that arrive close together (an import that saves 65 tracks) become one message.
/// The broadcaster sends it after a short quiet time. Thus the apps get the data again one time, not 65 times.
/// </summary>
internal sealed partial class ChangeBroadcaster(
    ChangeFeed feed,
    IHubContext<LibraryHub, ILibraryClient> hub,
    TimeProvider clock,
    ILogger<ChangeBroadcaster> logger) : BackgroundService
{
    /// <summary>The time to wait for more changes before the broadcaster sends the changes that it has.</summary>
    internal static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(250);

    /// <summary>The maximum wait for a change while more changes continue to arrive (a long import).</summary>
    internal static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // In .NET 11, an unhandled exception stops the host. A failed send must not stop the API.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var (changes, news) = await Gather(stoppingToken);
                if (feed.TakeOverflow())
                {
                    changes = changes with { Everything = true };
                    LogOverflowed();
                }
                // Send the change notices first. Thus the news arrives when the new data is available.
                if (!changes.IsEmpty)
                {
                    await hub.Clients.All.Changed(changes);
                    LogSent(changes.Playlists.Count, changes.Tracks, changes.Downloads);
                }
                if (news.Count > 0)
                {
                    await hub.Clients.All.Announced(Announcement.Summarize(news));
                    LogAnnounced(news.Count);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                LogSendFailed(exception);
            }
        }
    }

    /// <summary>Waits for a change, then merges the changes that follow until a quiet time occurs.</summary>
    private async Task<Update> Gather(CancellationToken stoppingToken)
    {
        var reader = feed.Reader;
        var changes = await reader.ReadAsync(stoppingToken);
        var deadline = clock.GetUtcNow() + MaxDelay;
        while (clock.GetUtcNow() < deadline)
        {
            using var quiet = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            quiet.CancelAfter(Settle);
            try
            {
                if (!await reader.WaitToReadAsync(quiet.Token)) break;
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                break; // A quiet time occurred: send now.
            }
            while (reader.TryRead(out var more)) changes = changes.Merge(more);
        }
        return changes;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Told apps: {Playlists} playlist(s), tracks {Tracks}, downloads {Downloads}")]
    private partial void LogSent(int playlists, bool tracks, bool downloads);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Told apps {Count} piece(s) of news")]
    private partial void LogAnnounced(int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Change notifications fell behind; telling apps everything may have changed")]
    private partial void LogOverflowed();

    [LoggerMessage(Level = LogLevel.Error, Message = "Couldn't send change notifications to apps")]
    private partial void LogSendFailed(Exception exception);
}
