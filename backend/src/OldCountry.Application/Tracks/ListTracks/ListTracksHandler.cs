using FluentValidation;
using Microsoft.EntityFrameworkCore;
using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Application.Abstractions.Persistence;
using OldCountry.Application.Common.Paging;

namespace OldCountry.Application.Tracks.ListTracks;

internal sealed class ListTracksHandler(IAppDbContext db) : IQueryHandler<ListTracksQuery, PagedResponse<TrackResponse>>
{
    public async Task<PagedResponse<TrackResponse>> Handle(ListTracksQuery query, CancellationToken cancellationToken)
    {
        var rows =
            from track in db.Tracks.AsNoTracking()
            join artist in db.Artists on track.ArtistId equals artist.Id
            select new { Track = track, ArtistName = artist.Name };

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // "%" and "_" in the search text are literal characters, not wildcards.
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
