using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using OldCountry.Api.Http;
using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Application.Common.Paging;
using OldCountry.Application.Tracks;
using OldCountry.Application.Tracks.ListTracks;

namespace OldCountry.Api.Endpoints.Tracks;

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
