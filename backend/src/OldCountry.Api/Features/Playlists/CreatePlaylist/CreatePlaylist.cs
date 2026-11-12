using Microsoft.AspNetCore.Http.HttpResults;
using OldCountry.Api.Common.Errors;
using OldCountry.Api.Common.Messaging;
using OldCountry.Api.Common.Validation;
using OldCountry.Api.Features.Playlists.GetPlaylist;
using OldCountry.Api.Persistence;
using OldCountry.Domain.Errors;
using OldCountry.Domain.Library;

namespace OldCountry.Api.Features.Playlists.CreatePlaylist;

public sealed record CreatePlaylistCommand(string Name, string? Description) : ICommand<CreatePlaylistResult>;

public readonly union CreatePlaylistResult(PlaylistSummaryResponse, ValidationError);

internal sealed partial class CreatePlaylistHandler(OldCountryDbContext db, ILogger<CreatePlaylistHandler> logger)
    : ICommandHandler<CreatePlaylistCommand, CreatePlaylistResult>
{
    public async Task<CreatePlaylistResult> Handle(CreatePlaylistCommand command, CancellationToken cancellationToken) =>
        Playlist.Create(command.Name, command.Description) switch
        {
            Playlist playlist => await Save(playlist, cancellationToken),
            ValidationError error => error,
        };

    private async Task<PlaylistSummaryResponse> Save(Playlist playlist, CancellationToken cancellationToken)
    {
        db.Playlists.Add(playlist);
        await db.SaveChangesAsync(cancellationToken);
        Log.Created(logger, playlist.Id.Value);
        return playlist.ToSummary();
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 1200, Level = LogLevel.Information, Message = "Created playlist {PlaylistId}")]
        public static partial void Created(ILogger logger, Guid playlistId);
    }
}

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
