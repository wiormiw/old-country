using System.Text.RegularExpressions;

namespace OldCountry.Application.YouTubeMusic;

/// <summary>Changes the text from the user into the one playlist URL that yt-dlp gets.</summary>
public partial class PlaylistSource
{
    public const string Liked = "liked";
    private const string LikedListId = "LM";

    public static bool IsLiked(string input) => string.Equals(input.Trim(), Liked, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when a stored list id is your liked songs. Liked songs need cookies.</summary>
    public static bool IsLikedList(string listId) => listId == LikedListId;

    public static Uri? ToUrl(string input)
    {
        input = input.Trim();
        if (IsLiked(input)) return Build(LikedListId);
        if (ListId().IsMatch(input)) return Build(input);

        if (!Uri.TryCreate(input, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps) return null;
        if (url.Host is not ("music.youtube.com" or "www.youtube.com" or "youtube.com" or "m.youtube.com")) return null;
        var list = System.Web.HttpUtility.ParseQueryString(url.Query)["list"];
        return list is not null && ListId().IsMatch(list) ? Build(list) : null;
    }

    /// <summary>The list id of a URL from <see cref="ToUrl"/>. This id links a library playlist to its source.</summary>
    public static string ListIdOf(Uri url) =>
        System.Web.HttpUtility.ParseQueryString(url.Query)["list"] ?? throw new ArgumentException("Not a playlist URL.", nameof(url));

    /// <summary>The URL for a list id that a playlist keeps, for the next sync.</summary>
    public static Uri FromListId(string listId) => Build(listId);

    private static Uri Build(string listId) => new($"https://music.youtube.com/playlist?list={listId}");

    [GeneratedRegex("^[A-Za-z0-9_-]{2,80}$")]
    private static partial Regex ListId();
}
