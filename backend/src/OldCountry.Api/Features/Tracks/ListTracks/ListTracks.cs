using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OldCountry.Api.Common;
using OldCountry.Api.Common.Messaging;
using OldCountry.Api.Common.Validation;
using OldCountry.Api.Persistence;

namespace OldCountry.Api.Features.Tracks.ListTracks;

/// <summary>Query string of the list endpoint: <c>?page=2&amp;pageSize=50&amp;search=dust</c>.</summary>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Tracks per page, 1-200.</param>
/// <param name="Search">Optional text matched against titles and artist names, ignoring case.</param>
public sealed record ListTracksRequest(int Page = 1, int PageSize = 50, string? Search = null);

internal sealed class ListTracksRequestValidator : AbstractValidator<ListTracksRequest>
{
    public const int MaxPageSize = 200;

    public ListTracksRequestValidator()
    {
        RuleFor(r => r.Page).GreaterThanOrEqualTo(1).WithMessage("Pages start at 1.");
        RuleFor(r => r.PageSize)
            .InclusiveBetween(1, MaxPageSize).WithMessage($"Ask for between 1 and {MaxPageSize} tracks per page.");
        RuleFor(r => r.Search).MaximumLength(200).WithMessage("Keep the search to 200 characters.");
    }
}

public sealed record ListTracksQuery(int Page, int PageSize, string? Search = null) : IQuery<PagedResponse<TrackResponse>>;

internal sealed class ListTracksHandler(OldCountryDbContext db) : IQueryHandler<ListTracksQuery, PagedResponse<TrackResponse>>
{
    public async Task<PagedResponse<TrackResponse>> Handle(ListTracksQuery query, CancellationToken cancellationToken)
    {
        var rows =
            from track in db.Tracks.AsNoTracking()
            join artist in db.Artists on track.ArtistId equals artist.Id
            select new { Track = track, ArtistName = artist.Name };

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // "%" and "_" in what the user typed are literal, not wildcards.
            var pattern = $"%{EscapeLike(query.Search.Trim())}%";
            rows = rows.Where(row => EF.Functions.ILike(row.Track.Title, pattern, @"\") || EF.Functions.ILike(row.ArtistName, pattern, @"\"));
        }

        var total = await rows.CountAsync(cancellationToken);
        var page = await rows
            .OrderBy(row => row.ArtistName).ThenBy(row => row.Track.Title).ThenBy(row => row.Track.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResponse<TrackResponse>(
            [.. page.Select(row => row.Track.ToResponse(row.ArtistName))], query.Page, query.PageSize, total);
    }

    private static string EscapeLike(string text) =>
        text.Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);
}

internal static class ListTracksEndpoint
{
    public static void Map(IEndpointRouteBuilder tracks) => tracks
        .MapGet("/", Handle)
        .Validate<ListTracksRequest>()
        .WithName("ListTracks")
        .WithSummary("List or search the library, by artist then title");

    private static async Task<Ok<PagedResponse<TrackResponse>>> Handle(
        [AsParameters] ListTracksRequest request,
        IQueryHandler<ListTracksQuery, PagedResponse<TrackResponse>> handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.Handle(new ListTracksQuery(request.Page, request.PageSize, request.Search), cancellationToken));
}
