using Microsoft.EntityFrameworkCore;
using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Application.Abstractions.Persistence;
using OldCountry.Domain.Errors;

namespace OldCountry.Application.Playlists.MovePlaylistEntry;

internal sealed class MovePlaylistEntryHandler(IAppDbContext db)
    : ICommandHandler<MovePlaylistEntryCommand, MovePlaylistEntryResult>
{
    public async Task<MovePlaylistEntryResult> Handle(MovePlaylistEntryCommand command, CancellationToken cancellationToken)
    {
        var playlist = await db.Playlists.SingleOrDefaultAsync(p => p.Id == command.Id, cancellationToken);
        if (playlist is null) return new NotFoundError("playlist", command.Id.ToString());
        if (!playlist.Move(command.From, command.To)) return new NotFoundError("playlist_entry", $"{command.Id}/{command.From}->{command.To}");

        await db.SaveChangesAsync(cancellationToken);
        return Done.Value;
    }
}
