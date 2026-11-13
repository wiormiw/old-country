using Microsoft.AspNetCore.SignalR;

namespace OldCountry.Api.Common.Realtime;

/// <summary>
/// Sends committed changes to every connected app. Changes arriving close together (an import
/// saving 65 songs, a playlist reordered in a hurry) are merged and sent once, after a short
/// quiet moment, so apps refetch once instead of 65 times.
/// </summary>
internal sealed partial class ChangeBroadcaster(
    ChangeFeed feed,
    IHubContext<LibraryHub, ILibraryClient> hub,
    TimeProvider clock,
    ILogger<ChangeBroadcaster> logger) : BackgroundService
{
    /// <summary>How long to wait for more changes before sending what has gathered.</summary>
    internal static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(250);

    /// <summary>The longest a change waits while changes keep coming (a long import).</summary>
    internal static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Unhandled exceptions stop the host in .NET 11; a failed send must not take the API down.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var changes = await Gather(stoppingToken);
                if (feed.TakeOverflow())
                {
                    changes = changes with { Everything = true };
                    LogOverflowed();
                }
                await hub.Clients.All.Changed(changes);
                LogSent(changes.Playlists.Count, changes.Tracks, changes.Downloads);
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

    /// <summary>Waits for a change, then merges in whatever follows until things settle.</summary>
    private async Task<LibraryChanges> Gather(CancellationToken stoppingToken)
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
                break; // quiet for a moment: send
            }
            while (reader.TryRead(out var more)) changes = changes.Merge(more);
        }
        return changes;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Told apps: {Playlists} playlist(s), tracks {Tracks}, downloads {Downloads}")]
    private partial void LogSent(int playlists, bool tracks, bool downloads);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Change notifications fell behind; telling apps everything may have changed")]
    private partial void LogOverflowed();

    [LoggerMessage(Level = LogLevel.Error, Message = "Couldn't send change notifications to apps")]
    private partial void LogSendFailed(Exception exception);
}
