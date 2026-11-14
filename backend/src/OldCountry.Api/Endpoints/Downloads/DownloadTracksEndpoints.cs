using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using OldCountry.Api.Http;
using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Application.Downloads;
using OldCountry.Application.Downloads.ListDownloads;
using OldCountry.Application.Downloads.QueueDownloads;
using OldCountry.Application.Downloads.QueuePlaylistDownloads;
using OldCountry.Domain.Errors;
using OldCountry.Domain.Playlists;
using OldCountry.Domain.YouTube;

namespace OldCountry.Api.Endpoints.Downloads;

internal static class DownloadTracksEndpoints
{
    public const string ListName = "ListYouTubeDownloads";

    public static void Map(IEndpointRouteBuilder youTube)
    {
        youTube.MapPost("/downloads", Queue)
            .Validate<DownloadTracksRequest>()
            .WithName("DownloadYouTubeTracks")
            .WithSummary("Download videos' audio into the library for offline play")
            .WithDescription("Queues the downloads and returns at once; poll the downloads list for progress. "
                + "A video already queued or downloading joins that job instead of starting another.");

        youTube.MapPost("/playlists/{id:guid}/downloads", QueuePlaylist)
            .WithName("DownloadYouTubePlaylist")
            .WithSummary("Download every song of a playlist that isn't downloaded yet")
            .WithDescription("Songs YouTube refused before for a lasting reason (Premium only, private, removed, age or region) "
                + "are skipped unless retrySkipped is true.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        youTube.MapGet("/downloads", List)
            .WithName(ListName)
            .WithSummary("Recent downloads and where each stands, newest first, and whether the queue is resting");
    }

    private static async Task<Results<AcceptedAtRoute<IReadOnlyList<DownloadStatus>>, ValidationProblem>> Queue(
        DownloadTracksRequest request, ICommandHandler<QueueDownloadsCommand, QueueDownloadsResult> handler, CancellationToken cancellationToken)
    {
        var videos = request.VideoIds.Select(id => new YouTubeVideoId(id)).ToList();
        return await handler.Handle(new QueueDownloadsCommand(videos), cancellationToken) switch
        {
            IReadOnlyList<DownloadStatus> jobs => TypedResults.AcceptedAtRoute(jobs, ListName),
            ValidationError invalid => invalid.ToProblem(),
        };
    }

    private static async Task<Results<AcceptedAtRoute<PlaylistDownloadReport>, ProblemHttpResult, ValidationProblem>> QueuePlaylist(
        Guid id, PlaylistDownloadRequest? request, ICommandHandler<QueuePlaylistDownloadsCommand, QueuePlaylistDownloadsResult> handler,
        CancellationToken cancellationToken) =>
        await handler.Handle(new QueuePlaylistDownloadsCommand(new PlaylistId(id), request?.RetrySkipped ?? false), cancellationToken) switch
        {
            PlaylistDownloadReport report => TypedResults.AcceptedAtRoute(report, ListName),
            NotFoundError missing => missing.ToProblem(),
            ValidationError invalid => invalid.ToProblem(),
        };

    private static async Task<Ok<DownloadsResponse>> List(IQueryHandler<ListDownloadsQuery, DownloadsResponse> handler, CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.Handle(new ListDownloadsQuery(), cancellationToken));
}
