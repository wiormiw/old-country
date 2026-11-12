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

/// <summary>A YouTube Music playlist to copy into the library as a new playlist.</summary>
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

/// <summary>What an import created.</summary>
/// <param name="PlaylistId">The new library playlist.</param>
/// <param name="Name">Its name, taken from YouTube Music.</param>
/// <param name="Entries">Entries in the new playlist.</param>
/// <param name="TracksCreated">Library tracks created for videos the library didn't have yet (not downloaded).</param>
/// <param name="TracksExisting">Videos that were already library tracks.</param>
/// <param name="Skipped">Videos left out: no duration reported, or past the playlist size limit.</param>
public sealed record ImportPlaylistReport(Guid PlaylistId, string Name, int Entries, int TracksCreated, int TracksExisting, int Skipped);

/// <summary>Holds no transaction while yt-dlp lists the playlist; the writes happen in one SaveChanges.</summary>
[NoTransaction]
public sealed record ImportPlaylistCommand(Uri PlaylistUrl) : ICommand<ImportPlaylistResult>;

public readonly union ImportPlaylistResult(ImportPlaylistReport, ExternalServiceError);

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

        var ids = source.Entries.Select(e => (YouTubeVideoId?)e.Id).Distinct().ToList();
        var known = await db.Tracks
            .Where(t => ids.Contains(t.YouTubeVideoId))
            .Select(t => new { t.YouTubeVideoId, t.Id })
            .ToDictionaryAsync(t => t.YouTubeVideoId!.Value, t => t.Id, cancellationToken);

        var created = new Dictionary<YouTubeVideoId, TrackId>();
        var order = new List<TrackId>(source.Entries.Count);
        int existing = 0, skipped = 0;
        foreach (var video in source.Entries)
        {
            if (order.Count == Playlist.MaxEntries)
            {
                skipped++;
                continue;
            }
            if (known.TryGetValue(video.Id, out var knownId) || created.TryGetValue(video.Id, out knownId))
            {
                if (known.ContainsKey(video.Id)) existing++;
                order.Add(knownId);
                continue;
            }

            // A YouTube-only track: streamable now, downloadable later.
            var artist = await artists.FindOrCreateAsync(Clip(video.Artist ?? UnknownArtist, Artist.NameMaxLength), cancellationToken);
            if (Track.Create(Clip(video.Title, Track.TitleMaxLength), artist.Id, video.Duration ?? TimeSpan.Zero, null, video.Id).Value is not Track track)
            {
                skipped++;
                continue;
            }
            db.Tracks.Add(track);
            created[video.Id] = track.Id;
            order.Add(track.Id);
        }

        var name = Clip(source.Title, Playlist.NameMaxLength);
        var playlist = (Playlist)Playlist.Create(name, $"Imported from YouTube Music: {command.PlaylistUrl}").Value!;
        playlist.Append(order, clock.GetUtcNow());
        db.Playlists.Add(playlist);
        await db.SaveChangesAsync(cancellationToken);

        Log.Imported(logger, playlist.Id.Value, order.Count, created.Count, skipped);
        return new ImportPlaylistReport(playlist.Id.Value, playlist.Name, order.Count, created.Count, existing, skipped);
    }

    private static string Clip(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];

    private static partial class Log
    {
        [LoggerMessage(EventId = 1330, Level = LogLevel.Information,
            Message = "Imported playlist {PlaylistId}: {Entries} entries, {TracksCreated} new tracks, {Skipped} skipped")]
        public static partial void Imported(ILogger logger, Guid playlistId, int entries, int tracksCreated, int skipped);
    }
}

internal static class ImportPlaylistEndpoint
{
    public static void Map(IEndpointRouteBuilder youTube) => youTube
        .MapPost("/imports", Handle)
        .Validate<ImportPlaylistRequest>()
        .WithName("ImportYouTubePlaylist")
        .WithSummary("Copy a YouTube Music playlist (or your liked songs) into a new library playlist")
        .WithDescription("Videos not yet in the library become YouTube-only tracks; download them later for offline play.")
        .ProducesProblem(StatusCodes.Status502BadGateway);

    private static async Task<Results<Created<ImportPlaylistReport>, ProblemHttpResult>> Handle(
        ImportPlaylistRequest request, ICommandHandler<ImportPlaylistCommand, ImportPlaylistResult> handler, CancellationToken cancellationToken) =>
        await handler.Handle(new ImportPlaylistCommand(PlaylistSource.ToUrl(request.Playlist)!), cancellationToken) switch
        {
            ImportPlaylistReport report => TypedResults.Created($"/api/playlists/{report.PlaylistId}", report),
            ExternalServiceError failed => failed.ToProblem(),
        };
}
