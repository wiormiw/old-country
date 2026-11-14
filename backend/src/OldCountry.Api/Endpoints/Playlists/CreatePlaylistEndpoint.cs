using Microsoft.AspNetCore.Http.HttpResults;
using OldCountry.Api.Http;
using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Application.Playlists;
using OldCountry.Application.Playlists.CreatePlaylist;
using OldCountry.Domain.Errors;

namespace OldCountry.Api.Endpoints.Playlists;

internal static class CreatePlaylistEndpoint
{
    public static void Map(IEndpointRouteBuilder playlists) => playlists
        .MapPost("/", Handle)
        .Validate<PlaylistDetailsRequest>()
        .WithName("CreatePlaylist")
        .WithSummary("Create an empty playlist");

    private static async Task<Results<CreatedAtRoute<PlaylistSummaryResponse>, ValidationProblem>> Handle(
        PlaylistDetailsRequest request, ICommandHandler<CreatePlaylistCommand, CreatePlaylistResult> handler, CancellationToken cancellationToken) =>
        await handler.Handle(new CreatePlaylistCommand(request.Name, request.Description), cancellationToken) switch
        {
            PlaylistSummaryResponse playlist => TypedResults.CreatedAtRoute(playlist, GetPlaylistEndpoint.Name, new { id = playlist.Id }),
            ValidationError invalid => invalid.ToProblem(),
        };
}
