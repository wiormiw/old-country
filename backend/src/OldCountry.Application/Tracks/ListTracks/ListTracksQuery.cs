using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Application.Common.Paging;

namespace OldCountry.Application.Tracks.ListTracks;

public sealed record ListTracksQuery(int Page, int PageSize, string? Search = null, bool IncludeTotal = true)
    : IQuery<PagedResponse<TrackResponse>>;
