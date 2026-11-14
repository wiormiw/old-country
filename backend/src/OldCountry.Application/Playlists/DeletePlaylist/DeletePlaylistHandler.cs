using Microsoft.EntityFrameworkCore;
using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Application.Abstractions.Persistence;
using OldCountry.Domain.Errors;

namespace OldCountry.Application.Playlists.DeletePlaylist;

internal sealed partial class DeletePlaylistHandler(IAppDbContext db, ILogger<DeletePlaylistHandler> logger)
    : ICommandHandler<DeletePlaylistCommand, DeletePlaylistResult>
{
    public async Task<DeletePlaylistResult> Handle(DeletePlaylistCommand command, CancellationToken cancellationToken)
    {
        // The entries of the playlist are in its own row, so one DELETE removes all. The tracks stay.
        var deleted = await db.Playlists.Where(p => p.Id == command.Id).ExecuteDeleteAsync(cancellationToken);
        if (deleted == 0) return new NotFoundError("playlist", command.Id.ToString());

        Log.Deleted(logger, command.Id.Value);
        return Done.Value;
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 1201, Level = LogLevel.Information, Message = "Deleted playlist {PlaylistId}")]
        public static partial void Deleted(ILogger logger, Guid playlistId);
    }
}
