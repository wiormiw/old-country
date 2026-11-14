using Microsoft.AspNetCore.Http.HttpResults;
using OldCountry.Api.Http;
using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Application.YouTubeMusic.ImportPlaylist;
using OldCountry.Application.YouTubeMusic.SyncPlaylist;
using OldCountry.Domain.Errors;
using OldCountry.Domain.Playlists;

namespace OldCountry.Api.Endpoints.YouTubeMusic;

internal static class SyncPlaylistEndpoint
{
    public static void Map(IEndpointRouteBuilder youTube) => youTube
        .MapPost("/playlists/{id:guid}/sync", Handle)
        .WithName("SyncYouTubePlaylist")
        .WithSummary("Sync an imported playlist with YouTube Music now")
        .WithDescription("Appends songs added on YouTube since the last sync; the playlist's name, description, order and removals "
            + "are kept. The server also does this on its own every YouTubeMusic:PlaylistSyncInterval.")
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status502BadGateway);

    private static async Task<Results<Ok<ImportPlaylistReport>, ProblemHttpResult, ValidationProblem>> Handle(
        Guid id, ICommandHandler<SyncPlaylistCommand, SyncPlaylistResult> handler, CancellationToken cancellationToken) =>
        await handler.Handle(new SyncPlaylistCommand(new PlaylistId(id)), cancellationToken) switch
        {
            ImportPlaylistReport report => TypedResults.Ok(report),
            NotFoundError missing => missing.ToProblem(),
            ValidationError invalid => invalid.ToProblem(),
            ExternalServiceError failed => failed.ToProblem(),
        };
}
