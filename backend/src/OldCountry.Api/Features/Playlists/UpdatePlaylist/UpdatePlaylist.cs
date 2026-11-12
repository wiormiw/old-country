using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OldCountry.Api.Common.Errors;
using OldCountry.Api.Common.Messaging;
using OldCountry.Api.Common.Validation;
using OldCountry.Api.Persistence;
using OldCountry.Domain.Common;
using OldCountry.Domain.Errors;

namespace OldCountry.Api.Features.Playlists.UpdatePlaylist;

public sealed record UpdatePlaylistCommand(PlaylistId Id, string Name, string? Description) : ICommand<UpdatePlaylistResult>;

public readonly union UpdatePlaylistResult(PlaylistSummaryResponse, NotFoundError, ValidationError);

internal sealed class UpdatePlaylistHandler(OldCountryDbContext db) : ICommandHandler<UpdatePlaylistCommand, UpdatePlaylistResult>
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

internal static class UpdatePlaylistEndpoint
{
    public static void Map(IEndpointRouteBuilder playlists) => playlists
        .MapPut("/{id:guid}", Handle)
        .Validate<PlaylistDetailsRequest>()
        .WithName("UpdatePlaylist")
        .WithSummary("Rename a playlist or change its description")
        .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<Results<Ok<PlaylistSummaryResponse>, ProblemHttpResult, ValidationProblem>> Handle(
        Guid id, PlaylistDetailsRequest request, ICommandHandler<UpdatePlaylistCommand, UpdatePlaylistResult> handler, CancellationToken cancellationToken) =>
        await handler.Handle(new UpdatePlaylistCommand(new PlaylistId(id), request.Name, request.Description), cancellationToken) switch
        {
            PlaylistSummaryResponse playlist => TypedResults.Ok(playlist),
            NotFoundError notFound => notFound.ToProblem(),
            ValidationError invalid => invalid.ToProblem(),
        };
}
