namespace OldCountry.Application.Common.YouTube;

/// <summary>
/// The rest after YouTube asks the server to slow down. Downloads and playlist syncs both wait for it.
/// Each rate limit in a row doubles the rest (to a maximum of 8 × <see cref="YouTubeMusicOptions.RateLimitPause"/>).
/// A successful download resets it. It is only in memory: after a restart the queue tries again.
/// </summary>
public sealed class YouTubeRateLimit
{
    private readonly Lock _lock = new();
    private int _inARow;

    public DateTimeOffset? Until { get; private set; }

    public string? Reason { get; private set; }

    public DateTimeOffset RateLimited(DateTimeOffset now, TimeSpan basePause, string reason)
    {
        lock (_lock)
        {
            _inARow = Math.Min(_inARow + 1, 4);
            Until = now + basePause * (1 << (_inARow - 1));
            Reason = reason;
            return Until.Value;
        }
    }

    public void Succeeded()
    {
        lock (_lock)
        {
            _inARow = 0;
            Until = null;
            Reason = null;
        }
    }

    public TimeSpan? Remaining(DateTimeOffset now) => Until is { } until && until > now ? until - now : null;
}
