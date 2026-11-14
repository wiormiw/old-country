using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using OldCountry.Api.Http;
using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Application.YouTubeMusic.SearchYouTubeMusic;
using OldCountry.Domain.Errors;

namespace OldCountry.Api.Endpoints.YouTubeMusic;

internal static class SearchYouTubeMusicEndpoint
{
    public static void Map(IEndpointRouteBuilder youTube) => youTube
        .MapGet("/search", Handle)
        .Validate<SearchYouTubeMusicRequest>()
        .WithName("SearchYouTubeMusic")
        .WithSummary("Search YouTube for songs")
        .ProducesProblem(StatusCodes.Status502BadGateway)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

    private static async Task<Results<Ok<IReadOnlyList<YouTubeSearchResult>>, ProblemHttpResult>> Handle(
        [AsParameters] SearchYouTubeMusicRequest request,
        IQueryHandler<SearchYouTubeMusicQuery, SearchYouTubeMusicResult> handler,
        CancellationToken cancellationToken) =>
        await handler.Handle(new SearchYouTubeMusicQuery(request.Query!, request.Limit), cancellationToken) switch
        {
            IReadOnlyList<YouTubeSearchResult> results => TypedResults.Ok(results),
            ExternalServiceError failed => failed.ToProblem(),
        };
}
