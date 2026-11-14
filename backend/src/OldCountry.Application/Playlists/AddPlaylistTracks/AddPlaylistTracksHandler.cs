using FluentValidation;
using Microsoft.EntityFrameworkCore;
using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Application.Abstractions.Persistence;
using OldCountry.Domain.Errors;

namespace OldCountry.Application.Playlists.AddPlaylistTracks;

internal sealed class AddPlaylistTracksHandler(IAppDbContext db, TimeProvider clock)
    : ICommandHandler<AddPlaylistTracksCommand, AddPlaylistTracksResult>
{
    public async Task<AddPlaylistTracksResult> Handle(AddPlaylistTracksCommand command, CancellationToken cancellationToken)
    {
        var playlist = await db.Playlists.SingleOrDefaultAsync(p => p.Id == command.Id, cancellationToken);
        if (playlist is null) return new NotFoundError("playlist", command.Id.ToString());

        // All or nothing: one unknown id refuses the full request. The error names each unknown id.
        var distinct = command.TrackIds.Distinct().ToList();
        var existing = await db.Tracks.Where(t => distinct.Contains(t.Id)).Select(t => t.Id).ToListAsync(cancellationToken);
        var unknown = distinct.Except(existing).ToList();
        if (unknown.Count > 0)
            return new ValidationError(new Dictionary<string, string[]>
            {
                ["trackIds"] = [.. unknown.Select(id => $"No track has id '{id}'.")],
            });

        if (playlist.Append([.. command.TrackIds], clock.GetUtcNow()) is { } tooMany) return tooMany;

        await db.SaveChangesAsync(cancellationToken);
        return playlist.ToSummary();
    }
}
