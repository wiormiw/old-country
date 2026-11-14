using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using OldCountry.Api.Http;
using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Application.YouTubeMusic;
using OldCountry.Application.YouTubeMusic.ImportPlaylist;
using OldCountry.Domain.Errors;

namespace OldCountry.Api.Endpoints.YouTubeMusic;

internal static class ImportPlaylistEndpoint
{
    public static void Map(IEndpointRouteBuilder youTube) => youTube
        .MapPost("/imports", Handle)
        .Validate<ImportPlaylistRequest>()
        .WithName("ImportYouTubePlaylist")
        .WithSummary("Import a YouTube Music playlist (or your liked songs), or sync one imported before")
        .WithDescription("The first import creates a library playlist (201). Importing the same playlist again syncs it (200): "
            + "songs added on YouTube since are appended; the library playlist's name, description, order and removals are kept. "
            + "Videos not yet in the library become YouTube-only tracks; download them later for offline play.")
        .ProducesYouTubeProblems();

    private static async Task<Results<Created<ImportPlaylistReport>, Ok<ImportPlaylistReport>, ProblemHttpResult>> Handle(
        ImportPlaylistRequest request, ICommandHandler<ImportPlaylistCommand, ImportPlaylistResult> handler, CancellationToken cancellationToken) =>
        await handler.Handle(new ImportPlaylistCommand(PlaylistSource.ToUrl(request.Playlist)!), cancellationToken) switch
        {
            ImportPlaylistReport { IsNew: true } report => TypedResults.Created($"/api/playlists/{report.PlaylistId}", report),
            ImportPlaylistReport report => TypedResults.Ok(report),
            ExternalServiceError failed => failed.ToProblem(),
        };
}
