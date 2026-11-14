using Microsoft.EntityFrameworkCore;
using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Application.Abstractions.Persistence;
using OldCountry.Domain.Errors;

namespace OldCountry.Application.Playlists.UpdatePlaylist;

internal sealed class UpdatePlaylistHandler(IAppDbContext db) : ICommandHandler<UpdatePlaylistCommand, UpdatePlaylistResult>
{
    public async Task<UpdatePlaylistResult> Handle(UpdatePlaylistCommand command, CancellationToken cancellationToken)
    {
        var playlist = await db.Playlists.SingleOrDefaultAsync(p => p.Id == command.Id, cancellationToken);
        if (playlist is null) return new NotFoundError("playlist", command.Id.ToString());
        if (playlist.Rename(command.Name, command.Description) is { } invalid) return invalid;

        await db.SaveChangesAsync(cancellationToken);
        return playlist.ToSummary();
    }
}
