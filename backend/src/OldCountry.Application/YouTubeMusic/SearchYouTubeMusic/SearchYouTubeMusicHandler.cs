using FluentValidation;
using Microsoft.EntityFrameworkCore;
using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Application.Abstractions.Persistence;
using OldCountry.Application.Abstractions.YouTube;

namespace OldCountry.Application.YouTubeMusic.SearchYouTubeMusic;

internal sealed class SearchYouTubeMusicHandler(IYtDlp ytDlp, IAppDbContext db)
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

        // One query marks each result that is already in the library.
        var ids = videos.Select(v => (Domain.YouTube.YouTubeVideoId?)v.Id).ToList();
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
