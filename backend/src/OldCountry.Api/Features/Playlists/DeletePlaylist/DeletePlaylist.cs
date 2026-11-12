using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OldCountry.Api.Common.Errors;
using OldCountry.Api.Common.Messaging;
using OldCountry.Api.Persistence;
using OldCountry.Domain.Common;
using OldCountry.Domain.Errors;

namespace OldCountry.Api.Features.Playlists.DeletePlaylist;

public sealed record DeletePlaylistCommand(PlaylistId Id) : ICommand<DeletePlaylistResult>;

public readonly union DeletePlaylistResult(Done, NotFoundError);

internal sealed partial class DeletePlaylistHandler(OldCountryDbContext db, ILogger<DeletePlaylistHandler> logger)
    : ICommandHandler<DeletePlaylistCommand, DeletePlaylistResult>
{
    public async Task<DeletePlaylistResult> Handle(DeletePlaylistCommand command, CancellationToken cancellationToken)
    {
        // The playlist's entries live in its own row, so one DELETE removes everything; the tracks stay.
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

internal static class DeletePlaylistEndpoint
{
    public static void Map(IEndpointRouteBuilder playlists) => playlists
        .MapDelete("/{id:guid}", Handle)
        .WithName("DeletePlaylist")
        .WithSummary("Delete a playlist (its tracks stay in the library)")
        .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<Results<NoContent, ProblemHttpResult>> Handle(
        Guid id, ICommandHandler<DeletePlaylistCommand, DeletePlaylistResult> handler, CancellationToken cancellationToken) =>
        await handler.Handle(new DeletePlaylistCommand(new PlaylistId(id)), cancellationToken) switch
        {
            Done => TypedResults.NoContent(),
            NotFoundError notFound => notFound.ToProblem(),
        };
}
