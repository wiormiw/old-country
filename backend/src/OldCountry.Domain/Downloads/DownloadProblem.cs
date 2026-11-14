namespace OldCountry.Domain.Downloads;

/// <summary>The last download of a track that failed, and the reason. A successful download clears it.</summary>
public sealed record DownloadProblem(DownloadRefusal Reason, string Message, DateTimeOffset At)
{
    public const int MessageMaxLength = 1000;

    /// <summary>
    /// True when a new attempt soon will not help (Premium, private, removed, age, region).
    /// "Download all" skips these tracks, unless the user asks to try them again.
    /// </summary>
    public bool IsLasting => Reason is not DownloadRefusal.Failed;

    public static DownloadProblem Of(DownloadRefusal reason, string message, DateTimeOffset at) =>
        new(reason, message.Length <= MessageMaxLength ? message : message[..MessageMaxLength], at);
}
