using System.Text.RegularExpressions;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OldCountry.Api.Common.Errors;
using OldCountry.Api.Common.Library;
using OldCountry.Api.Common.Messaging;
using OldCountry.Api.Common.Validation;
using OldCountry.Api.Persistence;
using OldCountry.Domain.Common;
using OldCountry.Domain.Errors;
using OldCountry.Domain.Library;

namespace OldCountry.Api.Features.YouTubeMusic.ImportPlaylist;

/// <summary>A YouTube Music playlist to import, or to sync when it was imported before.</summary>
public sealed record ImportPlaylistRequest
{
    /// <summary><c>liked</c> for your liked songs, a playlist URL, or a playlist id.</summary>
    public required string Playlist { get; init; }
}

/// <summary>Turns what the user typed into the one playlist URL yt-dlp is given.</summary>
internal static partial class PlaylistSource
{
    public const string Liked = "liked";
    private const string LikedListId = "LM";

    public static bool IsLiked(string input) => string.Equals(input.Trim(), Liked, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a stored list id is your liked songs, which need cookies.</summary>
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

    /// <summary>The list id of a URL made by <see cref="ToUrl"/>: what links a library playlist to its source.</summary>
    public static string ListIdOf(Uri url) =>
        System.Web.HttpUtility.ParseQueryString(url.Query)["list"] ?? throw new ArgumentException("Not a playlist URL.", nameof(url));

    /// <summary>The URL for a list id stored on a playlist, for syncing it again.</summary>
    public static Uri FromListId(string listId) => Build(listId);

    private static Uri Build(string listId) => new($"https://music.youtube.com/playlist?list={listId}");

    [GeneratedRegex("^[A-Za-z0-9_-]{2,80}$")]
    private static partial Regex ListId();
}

internal sealed class ImportPlaylistRequestValidator : AbstractValidator<ImportPlaylistRequest>
{
    public ImportPlaylistRequestValidator(IOptions<YouTubeMusicOptions> options)
    {
        RuleFor(r => r.Playlist)
            .NotEmpty().WithMessage("Say which playlist to import: \"liked\", a playlist URL, or a playlist id.")
            .Must(p => PlaylistSource.ToUrl(p) is not null)
            .WithMessage("That isn't a YouTube Music playlist. Use \"liked\", a URL with ?list=..., or the list id itself.");
        RuleFor(r => r.Playlist)
            .Must(_ => options.Value.CookiesFile is not null)
            .When(r => PlaylistSource.IsLiked(r.Playlist))
            .WithMessage("Importing liked songs needs your YouTube Music cookies: set YouTubeMusic:CookiesFile to an exported cookies.txt.");
    }
}

/// <summary>What an import did.</summary>
/// <param name="PlaylistId">The library playlist.</param>
/// <param name="Name">Its name.</param>
/// <param name="IsNew">
/// <c>true</c> when this import created the playlist; <c>false</c> when the YouTube playlist had
/// been imported before and this synced that library playlist instead.
/// </param>
/// <param name="Entries">Entries in the playlist now.</param>
/// <param name="Added">
/// Entries this import added: every song for a new playlist; for a sync, only songs added on
/// YouTube since (songs removed from the library playlist stay removed).
/// </param>
/// <param name="TracksCreated">Library tracks created for videos the library didn't have yet (not downloaded).</param>
/// <param name="TracksExisting">Videos that were already library tracks.</param>
/// <param name="Skipped">Videos left out: no duration reported, or past the playlist size limit.</param>
public sealed record ImportPlaylistReport(
    Guid PlaylistId, string Name, bool IsNew, int Entries, int Added, int TracksCreated, int TracksExisting, int Skipped);

/// <summary>Holds no transaction while yt-dlp lists the playlist; the writes happen in one SaveChanges.</summary>
[NoTransaction]
public sealed record ImportPlaylistCommand(Uri PlaylistUrl) : ICommand<ImportPlaylistResult>;

public readonly union ImportPlaylistResult(ImportPlaylistReport, ExternalServiceError);

/// <summary>
/// The first import of a YouTube playlist creates a library playlist linked to it. Importing it
/// again syncs that playlist: songs added on YouTube since are appended, while the library
/// playlist's name, description, order and removals are left as they are.
/// </summary>
internal sealed partial class ImportPlaylistHandler(
    IYtDlp ytDlp,
    OldCountryDbContext db,
    ArtistDirectory artists,
    TimeProvider clock,
    ILogger<ImportPlaylistHandler> logger) : ICommandHandler<ImportPlaylistCommand, ImportPlaylistResult>
{
    private const string UnknownArtist = "Unknown Artist";

    public async Task<ImportPlaylistResult> Handle(ImportPlaylistCommand command, CancellationToken cancellationToken)
    {
        YtPlaylist source;
        try
        {
            source = await ytDlp.GetPlaylistAsync(command.PlaylistUrl, cancellationToken);
        }
        catch (YtDlpException exception)
        {
            return exception.ToError();
        }

        var listId = PlaylistSource.ListIdOf(command.PlaylistUrl);
        var linked = await db.Playlists.SingleOrDefaultAsync(p => p.YouTubeListId == listId, cancellationToken)
            ?? await FindEarlierImportAsync(listId, source, cancellationToken);
        return linked is null
            ? await CreateAsync(command.PlaylistUrl, listId, source, cancellationToken)
            : await SyncAsync(linked, source, cancellationToken);
    }

    /// <summary>
    /// Playlists imported before imports were linked to their source, or linked to none: the
    /// unlinked playlist whose songs are at least half from this YouTube playlist is taken to be
    /// its earlier import (however it was renamed or re-described since) and linked to it now.
    /// </summary>
    private async Task<Playlist?> FindEarlierImportAsync(string listId, YtPlaylist source, CancellationToken cancellationToken)
    {
        var unlinked = await db.Playlists.Where(p => p.YouTubeListId == null).ToListAsync(cancellationToken);
        if (unlinked.Count == 0) return null;

        var trackIds = unlinked.SelectMany(p => p.Entries).Select(entry => entry.TrackId).Distinct().ToList();
        var videoOf = await db.Tracks
            .Where(t => trackIds.Contains(t.Id) && t.YouTubeVideoId != null)
            .Select(t => new { t.Id, Video = t.YouTubeVideoId!.Value })
            .ToDictionaryAsync(t => t.Id, t => t.Video, cancellationToken);
        var onYouTube = source.Entries.Select(video => video.Id).ToHashSet();

        var match = unlinked
            .Select(playlist =>
            {
                var songs = playlist.Entries.Select(entry => entry.TrackId).Distinct().ToList();
                var fromThisList = songs.Count(id => videoOf.TryGetValue(id, out var video) && onYouTube.Contains(video));
                return (Playlist: playlist, Shared: fromThisList, Songs: songs.Count);
            })
            .Where(c => c.Shared > 0 && c.Shared * 2 >= c.Songs)
            .OrderByDescending(c => c.Shared)
            .FirstOrDefault()
            .Playlist;
        if (match is null) return null;

        match.LinkToYouTube(listId);
        Log.Linked(logger, match.Id.Value, listId);
        return match;
    }

    private async Task<ImportPlaylistReport> CreateAsync(Uri url, string listId, YtPlaylist source, CancellationToken cancellationToken)
    {
        var resolved = await ResolveAsync(source.Entries, Playlist.MaxEntries, cancellationToken);

        var playlist = (Playlist)Playlist.Create(Clip(source.Title, Playlist.NameMaxLength), $"Imported from YouTube Music: {url}").Value!;
        playlist.LinkToYouTube(listId);
        playlist.AppendFromYouTube(resolved.Songs, clock.GetUtcNow());
        playlist.MarkSyncedFromYouTube(clock.GetUtcNow());
        db.Playlists.Add(playlist);
        await db.SaveChangesAsync(cancellationToken);

        Log.Imported(logger, playlist.Id.Value, resolved.Songs.Count, resolved.Created, resolved.Skipped);
        return Report(playlist, isNew: true, resolved);
    }

    private async Task<ImportPlaylistReport> SyncAsync(Playlist playlist, YtPlaylist source, CancellationToken cancellationToken)
    {
        // The YouTube videos behind the songs it holds now: those are never added again.
        var trackIds = playlist.Entries.Select(entry => entry.TrackId).Distinct().ToList();
        var present = (await db.Tracks
            .Where(t => trackIds.Contains(t.Id) && t.YouTubeVideoId != null)
            .Select(t => t.YouTubeVideoId!.Value)
            .ToListAsync(cancellationToken)).ToHashSet();

        var onYouTube = source.Entries.Select(video => video.Id).ToList();
        playlist.MarkTakenFromYouTube(onYouTube.Where(present.Contains));
        var fresh = playlist.NewOnYouTube(onYouTube, present).ToHashSet();
        var toAdd = source.Entries.Where(video => fresh.Contains(video.Id)).DistinctBy(video => video.Id).ToList();

        var resolved = await ResolveAsync(toAdd, Playlist.MaxEntries - playlist.Entries.Count, cancellationToken);
        playlist.AppendFromYouTube(resolved.Songs, clock.GetUtcNow());
        playlist.MarkSyncedFromYouTube(clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);

        Log.Synced(logger, playlist.Id.Value, resolved.Songs.Count, resolved.Created, resolved.Skipped);
        return Report(playlist, isNew: false, resolved);
    }

    private sealed record Resolved(List<(YouTubeVideoId Video, TrackId Track)> Songs, int Created, int Existing, int Skipped);

    /// <summary>
    /// The library track for each video, in order: an existing one, or a new YouTube-only track
    /// (streamable now, downloadable later). At most <paramref name="capacity"/> songs; videos past
    /// that, or without a duration, are skipped.
    /// </summary>
    private async Task<Resolved> ResolveAsync(IReadOnlyList<YtVideo> videos, int capacity, CancellationToken cancellationToken)
    {
        var ids = videos.Select(v => (YouTubeVideoId?)v.Id).Distinct().ToList();
        var known = await db.Tracks
            .Where(t => ids.Contains(t.YouTubeVideoId))
            .Select(t => new { t.YouTubeVideoId, t.Id })
            .ToDictionaryAsync(t => t.YouTubeVideoId!.Value, t => t.Id, cancellationToken);

        var created = new Dictionary<YouTubeVideoId, TrackId>();
        var songs = new List<(YouTubeVideoId, TrackId)>(Math.Min(videos.Count, Math.Max(capacity, 0)));
        int existing = 0, skipped = 0;
        foreach (var video in videos)
        {
            if (songs.Count >= capacity)
            {
                skipped++;
                continue;
            }
            if (known.TryGetValue(video.Id, out var trackId) || created.TryGetValue(video.Id, out trackId))
            {
                if (known.ContainsKey(video.Id)) existing++;
                songs.Add((video.Id, trackId));
                continue;
            }

            var artist = await artists.FindOrCreateAsync(Clip(video.Artist ?? UnknownArtist, Artist.NameMaxLength), cancellationToken);
            if (Track.Create(Clip(video.Title, Track.TitleMaxLength), artist.Id, video.Duration ?? TimeSpan.Zero, null, video.Id).Value is not Track track)
            {
                skipped++;
                continue;
            }
            db.Tracks.Add(track);
            created[video.Id] = track.Id;
            songs.Add((video.Id, track.Id));
        }
        return new Resolved(songs, created.Count, existing, skipped);
    }

    private static ImportPlaylistReport Report(Playlist playlist, bool isNew, Resolved resolved) =>
        new(playlist.Id.Value, playlist.Name, isNew, playlist.Entries.Count, resolved.Songs.Count,
            resolved.Created, resolved.Existing, resolved.Skipped);

    private static string Clip(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];

    private static partial class Log
    {
        [LoggerMessage(EventId = 1330, Level = LogLevel.Information,
            Message = "Imported playlist {PlaylistId}: {Entries} entries, {TracksCreated} new tracks, {Skipped} skipped")]
        public static partial void Imported(ILogger logger, Guid playlistId, int entries, int tracksCreated, int skipped);

        [LoggerMessage(EventId = 1331, Level = LogLevel.Information,
            Message = "Synced playlist {PlaylistId} from YouTube: {Added} added, {TracksCreated} new tracks, {Skipped} skipped")]
        public static partial void Synced(ILogger logger, Guid playlistId, int added, int tracksCreated, int skipped);

        [LoggerMessage(EventId = 1332, Level = LogLevel.Information,
            Message = "Linked playlist {PlaylistId} to YouTube playlist {ListId}: it holds mostly that playlist's songs")]
        public static partial void Linked(ILogger logger, Guid playlistId, string listId);
    }
}

internal static class ImportPlaylistEndpoint
{
    public static void Map(IEndpointRouteBuilder youTube) => youTube
        .MapPost("/imports", Handle)
        .Validate<ImportPlaylistRequest>()
        .WithName("ImportYouTubePlaylist")
        .WithSummary("Import a YouTube Music playlist (or your liked songs), or sync one imported before")
        .WithDescription("The first import creates a library playlist (201). Importing the same playlist again syncs it (200): "
            + "songs added on YouTube since are appended; the library playlist's name, description, order and removals are kept. "
            + "Videos not yet in the library become YouTube-only tracks; download them later for offline play.")
        .ProducesProblem(StatusCodes.Status502BadGateway);

    private static async Task<Results<Created<ImportPlaylistReport>, Ok<ImportPlaylistReport>, ProblemHttpResult>> Handle(
        ImportPlaylistRequest request, ICommandHandler<ImportPlaylistCommand, ImportPlaylistResult> handler, CancellationToken cancellationToken) =>
        await handler.Handle(new ImportPlaylistCommand(PlaylistSource.ToUrl(request.Playlist)!), cancellationToken) switch
        {
            ImportPlaylistReport { IsNew: true } report => TypedResults.Created($"/api/playlists/{report.PlaylistId}", report),
            ImportPlaylistReport report => TypedResults.Ok(report),
            ExternalServiceError failed => failed.ToProblem(),
        };
}
