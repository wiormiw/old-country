using Microsoft.EntityFrameworkCore;
using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Application.Abstractions.Persistence;
using OldCountry.Domain.Errors;

namespace OldCountry.Application.Playlists.RemovePlaylistEntry;

internal sealed class RemovePlaylistEntryHandler(IAppDbContext db)
    : ICommandHandler<RemovePlaylistEntryCommand, RemovePlaylistEntryResult>
{
    public async Task<RemovePlaylistEntryResult> Handle(RemovePlaylistEntryCommand command, CancellationToken cancellationToken)
    {
        var playlist = await db.Playlists.SingleOrDefaultAsync(p => p.Id == command.Id, cancellationToken);
        if (playlist is null) return new NotFoundError("playlist", command.Id.ToString());
        if (!playlist.RemoveAt(command.Position)) return new NotFoundError("playlist_entry", $"{command.Id}/{command.Position}");

        await db.SaveChangesAsync(cancellationToken);
        return Done.Value;
    }
}
