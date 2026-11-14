using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Application.Abstractions.Persistence;
using OldCountry.Domain.Errors;
using OldCountry.Domain.Playlists;

namespace OldCountry.Application.Playlists.CreatePlaylist;

internal sealed partial class CreatePlaylistHandler(IAppDbContext db, ILogger<CreatePlaylistHandler> logger)
    : ICommandHandler<CreatePlaylistCommand, CreatePlaylistResult>
{
    public async Task<CreatePlaylistResult> Handle(CreatePlaylistCommand command, CancellationToken cancellationToken) =>
        Playlist.Create(command.Name, command.Description) switch
        {
            Playlist playlist => await Save(playlist, cancellationToken),
            ValidationError error => error,
        };

    private async Task<PlaylistSummaryResponse> Save(Playlist playlist, CancellationToken cancellationToken)
    {
        db.Playlists.Add(playlist);
        await db.SaveChangesAsync(cancellationToken);
        Log.Created(logger, playlist.Id.Value);
        return playlist.ToSummary();
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 1200, Level = LogLevel.Information, Message = "Created playlist {PlaylistId}")]
        public static partial void Created(ILogger logger, Guid playlistId);
    }
}
