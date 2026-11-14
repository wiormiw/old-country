using System.Text.Json;
using OldCountry.Application.Abstractions.YouTube;
using OldCountry.Application.Common.Library;
using OldCountry.Domain.Tracks;
using OldCountry.Domain.YouTube;

namespace OldCountry.Infrastructure.YouTube;

/// <summary>Reads the JSON output of yt-dlp. It is separate from the process, so tests can use sample output.</summary>
internal static class YtDlpJson
{
    public static YtPlaylist ParsePlaylist(string json)
    {
        using var document = Parse(json);
        var root = document.RootElement;
        var entries = new List<YtVideo>();
        if (root.TryGetProperty("entries", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in list.EnumerateArray())
            {
                if (ParseVideo(entry) is { } video) entries.Add(video);
            }
        }
        return new YtPlaylist(String(root, "title") ?? "YouTube Music playlist", entries);
    }

    public static YtDownload ParseDownload(string output)
    {
        // With -O, the JSON is the last line that is not empty. The lines before it are other output of yt-dlp.
        var line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault()
            ?? throw new YtDlpException("youtube.bad_output", "yt-dlp finished without describing the download.");
        using var document = Parse(line);
        var root = document.RootElement;

        var video = ParseVideo(root) ?? throw new YtDlpException("youtube.bad_output", "yt-dlp described the download without a valid video id.");
        var path = String(root, "filepath");
        if (path is null && root.TryGetProperty("requested_downloads", out var downloads) && downloads.ValueKind == JsonValueKind.Array)
            path = downloads.EnumerateArray().Select(d => String(d, "filepath")).LastOrDefault(p => p is not null);
        if (path is null || !Path.IsPathFullyQualified(path))
            throw new YtDlpException("youtube.bad_output", "yt-dlp did not report where it saved the file.");

        return new YtDownload(path, video, (int)Math.Round(Number(root, "abr") ?? 0), (int)(Number(root, "asr") ?? 0));
    }

    public static YtVideo? ParseVideo(JsonElement element)
    {
        if (YouTubeVideoId.Create(String(element, "id")).Value is not YouTubeVideoId id) return null;
        var title = String(element, "track") ?? String(element, "title");
        if (title is null) return null;

        var duration = Number(element, "duration") is { } seconds and > 0 ? TimeSpan.FromSeconds(Math.Round(seconds)) : (TimeSpan?)null;
        return new YtVideo(id, title, ArtistOf(element), duration, ThumbnailOf(element));
    }

    /// <summary>The music fields first. A channel name with the YouTube " - Topic" suffix last.</summary>
    public static string? ArtistOf(JsonElement element)
    {
        if (element.TryGetProperty("artists", out var artists) && artists.ValueKind == JsonValueKind.Array
            && artists.EnumerateArray().Select(a => a.ValueKind == JsonValueKind.String ? a.GetString() : null).FirstOrDefault(a => !string.IsNullOrWhiteSpace(a)) is { } first)
            return first.Trim();
        return String(element, "artist") ?? String(element, "creator") ?? CleanChannel(String(element, "uploader") ?? String(element, "channel"));
    }

    public static string? CleanChannel(string? channel) =>
        channel is null ? null
        : channel.EndsWith(" - Topic", StringComparison.Ordinal) ? channel[..^" - Topic".Length].Trim()
        : channel;

    /// <summary>The last "ERROR:" line of yt-dlp, without the prefix. That line is the reason. The other lines are noise.</summary>
    public static string SummarizeError(string stderr)
    {
        var lines = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var error = lines.LastOrDefault(l => l.StartsWith("ERROR:", StringComparison.Ordinal)) ?? lines.LastOrDefault() ?? "yt-dlp failed without saying why.";
        error = error.StartsWith("ERROR:", StringComparison.Ordinal) ? error["ERROR:".Length..].Trim() : error;
        return error.Length <= 500 ? error : error[..500];
    }

    private static string? ThumbnailOf(JsonElement element)
    {
        if (element.TryGetProperty("thumbnails", out var thumbnails) && thumbnails.ValueKind == JsonValueKind.Array)
        {
            // yt-dlp lists the thumbnails smallest first.
            var best = thumbnails.EnumerateArray().Select(t => String(t, "url")).LastOrDefault(u => u is not null);
            if (best is not null) return best;
        }
        return String(element, "thumbnail");
    }

    private static JsonDocument Parse(string json)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new YtDlpException("youtube.bad_output", "yt-dlp returned output that isn't JSON.", exception);
        }
    }

    private static string? String(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;

    private static double? Number(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;

    /// <summary>The codec of a downloaded file, from its extension.</summary>
    public static AudioCodec CodecOf(string path) => AudioFormats.CodecOf(path);
}
