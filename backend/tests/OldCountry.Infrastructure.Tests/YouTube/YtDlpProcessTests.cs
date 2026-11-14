using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OldCountry.Application.Abstractions.YouTube;
using OldCountry.Application.Common.Telemetry;
using OldCountry.Application.Common.YouTube;
using OldCountry.Domain.YouTube;
using OldCountry.Infrastructure.YouTube;
using OldCountry.Tests.Shared;

namespace OldCountry.Infrastructure.Tests.YouTube;

/// <summary>
/// The real process code (start, arguments, exit codes, timeouts). Shell scripts take the place of yt-dlp,
/// so the tests run offline and give the same result each time.
/// </summary>
public sealed class YtDlpProcessTests : IDisposable
{
    private readonly string _directory = TestAudio.NewTempDirectory();

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private YtDlpProcess WithScript(string body, TimeSpan? timeout = null)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The stand-in scripts need a Unix shell.");
        var script = Path.Combine(_directory, "fake-yt-dlp");
        File.WriteAllText(script, $"#!/bin/sh\n{body}\n");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return Process(script, timeout);
    }

    private static YtDlpProcess Process(string path, TimeSpan? timeout = null) =>
        new(Options.Create(new YouTubeMusicOptions { YtDlpPath = path, CommandTimeout = timeout ?? TimeSpan.FromSeconds(30) }),
            new AppMetrics(new TestMeterFactory()), NullLogger<YtDlpProcess>.Instance);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Output_is_parsed_and_arguments_arrive_intact()
    {
        // Send the search argument back as the title. This shows that no shell changed the spaces, the apostrophe
        // or the ampersand. (Double quotes would also stay correct, but they would break the JSON of this script.)
        var ytDlp = WithScript("""
            for last; do :; done
            printf '{"title":"t","entries":[{"id":"dQw4w9WgXcQ","title":"%s","duration":10}]}' "$last"
            """);

        var results = await ytDlp.SearchAsync("it's $HOME; & spaced", 3, Ct);

        results.ShouldHaveSingleItem().Title.ShouldBe("ytsearch3:it's $HOME; & spaced");
    }

    [Fact]
    public async Task The_configured_javascript_runtime_replaces_yt_dlps_default()
    {
        // Send each argument back as the title.
        var ytDlp = WithScript("""printf '{"title":"t","entries":[{"id":"dQw4w9WgXcQ","title":"%s","duration":10}]}' "$*" """);

        var results = await ytDlp.SearchAsync("x", 1, Ct);

        results.ShouldHaveSingleItem().Title.ShouldContain("--no-js-runtimes --js-runtimes node");
    }

    [Fact]
    public async Task A_non_zero_exit_becomes_an_error_with_yt_dlps_reason_and_what_kind_of_refusal_it_is()
    {
        var ytDlp = WithScript("""echo "ERROR: [youtube] abc: Video unavailable" >&2; exit 1""");

        var error = await Should.ThrowAsync<YtDlpException>(() => ytDlp.SearchAsync("x", 1, Ct));

        error.Code.ShouldBe("youtube.unavailable");
        error.Message.ShouldBe("[youtube] abc: Video unavailable");
    }

    [Fact]
    public async Task A_failure_is_retried_once_and_a_second_success_wins()
    {
        // Fails the first time and succeeds the second time. A marker file keeps the first attempt.
        var marker = Path.Combine(_directory, "tried");
        var ytDlp = WithScript($$"""
            if [ -f '{{marker}}' ]; then
              printf '{"title":"t","entries":[{"id":"dQw4w9WgXcQ","title":"second try","duration":10}]}'
            else
              touch '{{marker}}'; echo "ERROR: HTTP Error 403: Forbidden" >&2; exit 1
            fi
            """);

        var results = await ytDlp.SearchAsync("x", 1, Ct);

        results.ShouldHaveSingleItem().Title.ShouldBe("second try");
    }

    [Fact]
    public async Task A_missing_binary_says_how_to_fix_it()
    {
        var error = await Should.ThrowAsync<YtDlpException>(() => Process("/nonexistent/yt-dlp").SearchAsync("x", 1, Ct));

        error.Code.ShouldBe("youtube.ytdlp_missing");
        error.Message.ShouldContain("YouTubeMusic:YtDlpPath");
    }

    [Fact]
    public async Task A_run_past_the_timeout_is_stopped()
    {
        var ytDlp = WithScript("sleep 30", timeout: TimeSpan.FromMilliseconds(300));

        var error = await Should.ThrowAsync<YtDlpException>(() => ytDlp.SearchAsync("x", 1, Ct));

        error.Code.ShouldBe("youtube.timeout");
    }

    [Fact]
    public async Task Fetching_returns_the_path_yt_dlp_printed()
    {
        var file = Path.Combine(_directory, "dQw4w9WgXcQ.webm");
        File.WriteAllBytes(file, [1, 2, 3]);
        var ytDlp = WithScript($"echo '{file}'");

        (await ytDlp.FetchAudioAsync(new YouTubeVideoId("dQw4w9WgXcQ"), _directory, Ct)).ShouldBe(file);
    }
}
