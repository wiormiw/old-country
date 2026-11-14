using FluentValidation;

namespace OldCountry.Application.Common.YouTube;

internal sealed class YouTubeMusicOptionsValidator : AbstractValidator<YouTubeMusicOptions>
{
    public YouTubeMusicOptionsValidator()
    {
        RuleFor(o => o.YtDlpPath).NotEmpty().WithMessage("YtDlpPath can't be empty; use \"yt-dlp\" to find it on PATH.");
        RuleFor(o => o.JsRuntime)
            .Must(BeAKnownRuntime).When(o => !string.IsNullOrEmpty(o.JsRuntime))
            .WithMessage("JsRuntime must be node, deno, quickjs or bun, optionally followed by :<path> (or empty for yt-dlp's default).");
        RuleFor(o => o.CookiesFile!)
            .Must(Path.IsPathFullyQualified).When(o => o.CookiesFile is not null)
            .WithMessage("CookiesFile must be an absolute path to a cookies.txt file.");
        RuleFor(o => o.DownloadFolder!)
            .Must(Path.IsPathFullyQualified).When(o => o.DownloadFolder is not null)
            .WithMessage("DownloadFolder must be an absolute path.");
        RuleFor(o => o.StreamCacheFolder!)
            .Must(Path.IsPathFullyQualified).When(o => o.StreamCacheFolder is not null)
            .WithMessage("StreamCacheFolder must be an absolute path.");
        RuleFor(o => o.StreamCacheMaxMegabytes)
            .InclusiveBetween(64, 1024 * 1024).WithMessage("StreamCacheMaxMegabytes must be between 64 (MB) and 1048576 (1 TB).");
        RuleFor(o => o.AudioFormat)
            .Must(YouTubeMusicOptions.AudioFormats.Contains)
            .WithMessage($"AudioFormat must be one of: {string.Join(", ", YouTubeMusicOptions.AudioFormats)}.");
        RuleFor(o => o.CommandTimeout)
            .InclusiveBetween(TimeSpan.FromSeconds(10), TimeSpan.FromHours(1))
            .WithMessage("CommandTimeout must be between 10 seconds and 1 hour.");
        RuleFor(o => o.DownloadPause)
            .InclusiveBetween(TimeSpan.Zero, TimeSpan.FromMinutes(10))
            .WithMessage("DownloadPause must be between 0 and 10 minutes.");
        RuleFor(o => o.PlaylistSyncInterval)
            .Must(t => t == TimeSpan.Zero || (t >= TimeSpan.FromMinutes(10) && t <= TimeSpan.FromDays(7)))
            .WithMessage("PlaylistSyncInterval must be 00:00:00 (off) or between 10 minutes and 7 days.");
        RuleFor(o => o.RateLimitPause)
            .InclusiveBetween(TimeSpan.FromSeconds(1), TimeSpan.FromHours(6))
            .WithMessage("RateLimitPause must be between 1 second and 6 hours.");
    }

    private static readonly string[] Runtimes = ["node", "deno", "quickjs", "bun"];

    private static bool BeAKnownRuntime(string value)
    {
        var name = value.Split(':', 2)[0];
        var hasEmptyPath = value.Length == name.Length + 1;
        return Runtimes.Contains(name, StringComparer.Ordinal) && !hasEmptyPath;
    }
}
