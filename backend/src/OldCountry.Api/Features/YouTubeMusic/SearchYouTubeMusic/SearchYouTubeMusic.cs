using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OldCountry.Api.Common.Errors;
using OldCountry.Api.Common.Messaging;
using OldCountry.Api.Common.Validation;
using OldCountry.Api.Persistence;
using OldCountry.Domain.Errors;

namespace OldCountry.Api.Features.YouTubeMusic.SearchYouTubeMusic;

/// <summary>Query string of the search endpoint: <c>?query=...&amp;limit=20</c>.</summary>
/// <param name="Query">What to search for.</param>
/// <param name="Limit">How many results, 1-50.</param>
public sealed record SearchYouTubeMusicRequest(string? Query, int Limit = 20);

internal sealed class SearchYouTubeMusicRequestValidator : AbstractValidator<SearchYouTubeMusicRequest>
{
    public const int MaxLimit = 50;

    public SearchYouTubeMusicRequestValidator()
    {
        RuleFor(r => r.Query)
            .NotEmpty().WithMessage("Type something to search for.")
            .MaximumLength(200).WithMessage("Keep the search to 200 characters.");
        RuleFor(r => r.Limit).InclusiveBetween(1, MaxLimit).WithMessage($"Ask for between 1 and {MaxLimit} results.");
    }
}

/// <summary>One search hit, and whether it is already in the library.</summary>
/// <param name="VideoId">The YouTube video id; use it to stream, download or add the track.</param>
/// <param name="Title">Video title.</param>
/// <param name="Artist">Artist or channel name, when known.</param>
/// <param name="Duration">Length, when known.</param>
/// <param name="ThumbnailUrl">Largest thumbnail, when known.</param>
/// <param name="TrackId">The library track linked to this video, if any.</param>
/// <param name="IsAvailableOffline">Whether that library track has a local file.</param>
public sealed record YouTubeSearchResult(
    string VideoId,
    string Title,
    string? Artist,
    TimeSpan? Duration,
    string? ThumbnailUrl,
    Guid? TrackId,
    bool IsAvailableOffline);

public sealed record SearchYouTubeMusicQuery(string Query, int Limit) : IQuery<SearchYouTubeMusicResult>;

public readonly union SearchYouTubeMusicResult(IReadOnlyList<YouTubeSearchResult>, ExternalServiceError);

internal sealed class SearchYouTubeMusicHandler(IYtDlp ytDlp, OldCountryDbContext db)
    : IQueryHandler<SearchYouTubeMusicQuery, SearchYouTubeMusicResult>
{
    public async Task<SearchYouTubeMusicResult> Handle(SearchYouTubeMusicQuery query, CancellationToken cancellationToken)
    {
        IReadOnlyList<YtVideo> videos;
        try
        {
            videos = await ytDlp.SearchAsync(query.Query, query.Limit, cancellationToken);
        }
        catch (YtDlpException exception)
        {
            return exception.ToError();
        }

        // One query marks every hit that is already in the library.
        var ids = videos.Select(v => (Domain.Library.YouTubeVideoId?)v.Id).ToList();
        var known = await db.Tracks.AsNoTracking()
            .Where(t => ids.Contains(t.YouTubeVideoId))
            .Select(t => new { t.YouTubeVideoId, t.Id, Offline = t.LocalFile != null })
            .ToDictionaryAsync(t => t.YouTubeVideoId!.Value, cancellationToken);

        return new SearchYouTubeMusicResult([.. videos.Select(v =>
        {
            var track = known.GetValueOrDefault(v.Id);
            return new YouTubeSearchResult(v.Id.Value, v.Title, v.Artist, v.Duration, v.ThumbnailUrl, track?.Id.Value, track?.Offline ?? false);
        })]);
    }
}

internal static class SearchYouTubeMusicEndpoint
{
    public static void Map(IEndpointRouteBuilder youTube) => youTube
        .MapGet("/search", Handle)
        .Validate<SearchYouTubeMusicRequest>()
        .WithName("SearchYouTubeMusic")
        .WithSummary("Search YouTube for songs")
        .ProducesProblem(StatusCodes.Status502BadGateway);

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
