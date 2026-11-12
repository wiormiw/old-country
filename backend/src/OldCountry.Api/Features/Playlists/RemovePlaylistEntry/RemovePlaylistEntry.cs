using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OldCountry.Api.Common.Errors;
using OldCountry.Api.Common.Messaging;
using OldCountry.Api.Persistence;
using OldCountry.Domain.Common;
using OldCountry.Domain.Errors;

namespace OldCountry.Api.Features.Playlists.RemovePlaylistEntry;

/// <summary>Removes the entry at a position, not every copy of a track.</summary>
public sealed record RemovePlaylistEntryCommand(PlaylistId Id, int Position) : ICommand<RemovePlaylistEntryResult>;

public readonly union RemovePlaylistEntryResult(Done, NotFoundError);

internal sealed class RemovePlaylistEntryHandler(OldCountryDbContext db)
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

internal static class RemovePlaylistEntryEndpoint
{
    public static void Map(IEndpointRouteBuilder playlists) => playlists
        .MapDelete("/{id:guid}/tracks/{position:int}", Handle)
        .WithName("RemovePlaylistEntry")
        .WithSummary("Remove the entry at a position from a playlist")
        .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<Results<NoContent, ProblemHttpResult>> Handle(
        Guid id, int position, ICommandHandler<RemovePlaylistEntryCommand, RemovePlaylistEntryResult> handler, CancellationToken cancellationToken) =>
        await handler.Handle(new RemovePlaylistEntryCommand(new PlaylistId(id), position), cancellationToken) switch
        {
            Done => TypedResults.NoContent(),
            NotFoundError notFound => notFound.ToProblem(),
        };
}
