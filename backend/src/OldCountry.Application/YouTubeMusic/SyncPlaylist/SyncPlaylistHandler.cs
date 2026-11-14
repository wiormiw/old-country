using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Application.Abstractions.Persistence;
using OldCountry.Application.Common.YouTube;
using OldCountry.Application.YouTubeMusic.ImportPlaylist;
using OldCountry.Domain.Errors;

namespace OldCountry.Application.YouTubeMusic.SyncPlaylist;

internal sealed class SyncPlaylistHandler(
    IAppDbContext db,
    IOptions<YouTubeMusicOptions> options,
    ICommandHandler<ImportPlaylistCommand, ImportPlaylistResult> import) : ICommandHandler<SyncPlaylistCommand, SyncPlaylistResult>
{
    public async Task<SyncPlaylistResult> Handle(SyncPlaylistCommand command, CancellationToken cancellationToken)
    {
        var playlist = await db.Playlists.AsNoTracking()
            .Where(p => p.Id == command.Id)
            .Select(p => new { p.YouTubeListId })
            .SingleOrDefaultAsync(cancellationToken);
        if (playlist is null) return new NotFoundError("playlist", command.Id.ToString());
        if (playlist.YouTubeListId is not { } listId)
            return ValidationError.For("playlist", "This playlist was not imported from YouTube Music, so there is nothing to sync it with.");
        if (PlaylistSource.IsLikedList(listId) && options.Value.CookiesFile is null)
            return ValidationError.For("playlist", "Syncing liked songs needs your YouTube Music cookies: set YouTubeMusic:CookiesFile.");

        return await import.Handle(new ImportPlaylistCommand(PlaylistSource.FromListId(listId)), cancellationToken) switch
        {
            ImportPlaylistReport report => report,
            ExternalServiceError failed => failed,
        };
    }
}
