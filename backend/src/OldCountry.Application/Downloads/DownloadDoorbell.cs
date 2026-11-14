using System.Threading.Channels;

namespace OldCountry.Application.Downloads;

/// <summary>
/// Wakes the download worker when jobs are queued. It is a channel that holds a maximum of one ring,
/// so many requests in a row wake the worker one time. The jobs are in the database.
/// The ring only tells the worker to look now, before its next periodic check.
/// </summary>
public sealed class DownloadDoorbell
{
    private readonly Channel<bool> _rings = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    public void Ring() => _rings.Writer.TryWrite(true);

    /// <summary>Returns when the doorbell rings, or after <paramref name="timeout"/>.</summary>
    public async Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        wait.CancelAfter(timeout);
        try
        {
            await _rings.Reader.ReadAsync(wait.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The timeout occurred: it is time for the periodic check.
        }
    }
}
