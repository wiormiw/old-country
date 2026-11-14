using FluentValidation;
using Microsoft.EntityFrameworkCore;
using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Application.Abstractions.Persistence;
using OldCountry.Application.Abstractions.YouTube;
using OldCountry.Application.Common.Library;
using OldCountry.Domain.Artists;
using OldCountry.Domain.Playlists;
using OldCountry.Domain.Tracks;
using OldCountry.Domain.YouTube;

namespace OldCountry.Application.YouTubeMusic.ImportPlaylist;

/// <summary>
/// The first import of a YouTube playlist makes a library playlist that is linked to it.
/// A new import of the same playlist syncs that library playlist: it adds the tracks that were added on YouTube since.
/// The name, the description, the order and the removals of the library playlist stay.
/// </summary>
internal sealed partial class ImportPlaylistHandler(
    IYtDlp ytDlp,
    IAppDbContext db,
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
    /// For playlists that were imported before the import kept a link to the source.
    /// An unlinked playlist with at least half of its tracks from this YouTube playlist is its earlier import,
    /// also if the user changed its name or description. This method links it now.
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
        // The YouTube videos of the tracks that the playlist holds now. The import never adds them again.
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
    /// The library track of each video, in order: an existing track, or a new YouTube-only track
    /// (it can play now and download later). At most <paramref name="capacity"/> tracks.
    /// The method skips the videos after that limit, and the videos without a duration.
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
