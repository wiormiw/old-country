using OldCountry.Domain.Downloads;

namespace OldCountry.Application.Common.YouTube;

/// <summary>
/// Changes the error text of yt-dlp into a stable code. Thus the callers can act on the reason for a refusal:
/// skip a Premium-only track, pause the queue for a rate limit, or try a single failure again.
/// The text of yt-dlp comes from YouTube and changes. The phrases below are the ones that occurred in 2025-26.
/// </summary>
public static class YouTubeRefusals
{
    public const string PremiumOnly = "youtube.premium_only";
    public const string Private = "youtube.private";
    public const string Unavailable = "youtube.unavailable";
    public const string AgeRestricted = "youtube.age_restricted";
    public const string RegionBlocked = "youtube.region_blocked";
    public const string RateLimited = "youtube.rate_limited";
    public const string Failed = "youtube.failed";

    // The order is important: the first match wins (a Premium message can also say "unavailable").
    private static readonly (string Code, string[] Phrases)[] Rules =
    [
        (RateLimited, ["http error 429", "too many requests", "not a bot", "rate-limited", "rate limited", "rate limit"]),
        (PremiumOnly, ["music premium", "premium members", "requires payment", "members-only", "join this channel"]),
        (AgeRestricted, ["confirm your age", "age-restricted", "age restricted", "inappropriate for some users"]),
        (RegionBlocked, ["available in your country", "blocked it in your country", "geo restriction", "geo-restricted"]),
        (Private, ["private video", "video is private"]),
        (Unavailable, ["video unavailable", "video is unavailable", "has been removed", "no longer available",
            "account associated with this video has been terminated", "does not exist"]),
    ];

    public static string Classify(string reason)
    {
        var text = reason.ToLowerInvariant().Replace('’', '\'');
        foreach (var (code, phrases) in Rules)
        {
            if (phrases.Any(text.Contains)) return code;
        }
        return Failed;
    }

    /// <summary>
    /// The reason to keep on the track, or <c>null</c> when the failure is not about the video:
    /// a rate limit (about this server), a missing yt-dlp, or a timeout (about the server).
    /// </summary>
    public static DownloadRefusal? RefusalOf(string code) => code switch
    {
        PremiumOnly => DownloadRefusal.PremiumOnly,
        Private => DownloadRefusal.Private,
        Unavailable => DownloadRefusal.Unavailable,
        AgeRestricted => DownloadRefusal.AgeRestricted,
        RegionBlocked => DownloadRefusal.RegionBlocked,
        Failed => DownloadRefusal.Failed,
        _ => null,
    };
}
