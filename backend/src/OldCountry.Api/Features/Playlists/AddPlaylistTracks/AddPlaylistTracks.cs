using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OldCountry.Api.Common.Errors;
using OldCountry.Api.Common.Messaging;
using OldCountry.Api.Common.Validation;
using OldCountry.Api.Persistence;
using OldCountry.Domain.Common;
using OldCountry.Domain.Errors;

namespace OldCountry.Api.Features.Playlists.AddPlaylistTracks;

/// <summary>Tracks to append to the end of a playlist, in this order.</summary>
public sealed record AddPlaylistTracksRequest
{
    /// <summary>Ids of library tracks, 1-500 per request. Duplicates are allowed and kept.</summary>
    public required IReadOnlyList<Guid> TrackIds { get; init; }
}

internal sealed class AddPlaylistTracksRequestValidator : AbstractValidator<AddPlaylistTracksRequest>
{
    public const int MaxPerRequest = 500;

    public AddPlaylistTracksRequestValidator()
    {
        RuleFor(r => r.TrackIds)
            .NotEmpty().WithMessage("Pick at least one track to add.")
            .Must(ids => ids.Count <= MaxPerRequest).WithMessage($"Add at most {MaxPerRequest} tracks at a time.");
        RuleForEach(r => r.TrackIds)
            .NotEqual(Guid.Empty).WithMessage("Track ids can't be the empty GUID.");
    }
}

public sealed record AddPlaylistTracksCommand(PlaylistId Id, IReadOnlyList<TrackId> TrackIds) : ICommand<AddPlaylistTracksResult>;

public readonly union AddPlaylistTracksResult(PlaylistSummaryResponse, NotFoundError, ValidationError);

internal sealed class AddPlaylistTracksHandler(OldCountryDbContext db, TimeProvider clock)
    : ICommandHandler<AddPlaylistTracksCommand, AddPlaylistTracksResult>
{
    public async Task<AddPlaylistTracksResult> Handle(AddPlaylistTracksCommand command, CancellationToken cancellationToken)
    {
        var playlist = await db.Playlists.SingleOrDefaultAsync(p => p.Id == command.Id, cancellationToken);
        if (playlist is null) return new NotFoundError("playlist", command.Id.ToString());

        // All or nothing: one unknown id refuses the whole request, naming every unknown id.
        var distinct = command.TrackIds.Distinct().ToList();
        var existing = await db.Tracks.Where(t => distinct.Contains(t.Id)).Select(t => t.Id).ToListAsync(cancellationToken);
        var unknown = distinct.Except(existing).ToList();
        if (unknown.Count > 0)
            return new ValidationError(new Dictionary<string, string[]>
            {
                ["trackIds"] = [.. unknown.Select(id => $"No track has id '{id}'.")],
            });

        if (playlist.Append([.. command.TrackIds], clock.GetUtcNow()) is { } tooMany) return tooMany;

        await db.SaveChangesAsync(cancellationToken);
        return playlist.ToSummary();
    }
}

internal static class AddPlaylistTracksEndpoint
{
    public static void Map(IEndpointRouteBuilder playlists) => playlists
        .MapPost("/{id:guid}/tracks", Handle)
        .Validate<AddPlaylistTracksRequest>()
        .WithName("AddPlaylistTracks")
        .WithSummary("Append tracks to a playlist")
        .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<Results<Ok<PlaylistSummaryResponse>, ProblemHttpResult, ValidationProblem>> Handle(
        Guid id, AddPlaylistTracksRequest request, ICommandHandler<AddPlaylistTracksCommand, AddPlaylistTracksResult> handler,
        CancellationToken cancellationToken) =>
        await handler.Handle(new AddPlaylistTracksCommand(new PlaylistId(id), [.. request.TrackIds.Select(t => new TrackId(t))]), cancellationToken) switch
        {
            PlaylistSummaryResponse playlist => TypedResults.Ok(playlist),
            NotFoundError notFound => notFound.ToProblem(),
            ValidationError invalid => invalid.ToProblem(),
        };
}
