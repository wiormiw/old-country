using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OldCountry.Api.Common.Errors;
using OldCountry.Api.Common.Messaging;
using OldCountry.Api.Common.Validation;
using OldCountry.Api.Persistence;
using OldCountry.Domain.Common;
using OldCountry.Domain.Errors;

namespace OldCountry.Api.Features.Playlists.MovePlaylistEntry;

/// <summary>Moves one entry; the entries in between shift by one.</summary>
public sealed record MovePlaylistEntryRequest
{
    /// <summary>Current zero-based position of the entry.</summary>
    public required int From { get; init; }

    /// <summary>Zero-based position it should end up at.</summary>
    public required int To { get; init; }
}

internal sealed class MovePlaylistEntryRequestValidator : AbstractValidator<MovePlaylistEntryRequest>
{
    public MovePlaylistEntryRequestValidator()
    {
        RuleFor(r => r.From).GreaterThanOrEqualTo(0).WithMessage("Positions start at 0.");
        RuleFor(r => r.To).GreaterThanOrEqualTo(0).WithMessage("Positions start at 0.");
        RuleFor(r => r.To).NotEqual(r => r.From).WithMessage("The entry is already at that position.");
    }
}

public sealed record MovePlaylistEntryCommand(PlaylistId Id, int From, int To) : ICommand<MovePlaylistEntryResult>;

public readonly union MovePlaylistEntryResult(Done, NotFoundError);

internal sealed class MovePlaylistEntryHandler(OldCountryDbContext db)
    : ICommandHandler<MovePlaylistEntryCommand, MovePlaylistEntryResult>
{
    public async Task<MovePlaylistEntryResult> Handle(MovePlaylistEntryCommand command, CancellationToken cancellationToken)
    {
        var playlist = await db.Playlists.SingleOrDefaultAsync(p => p.Id == command.Id, cancellationToken);
        if (playlist is null) return new NotFoundError("playlist", command.Id.ToString());
        if (!playlist.Move(command.From, command.To)) return new NotFoundError("playlist_entry", $"{command.Id}/{command.From}->{command.To}");

        await db.SaveChangesAsync(cancellationToken);
        return Done.Value;
    }
}

internal static class MovePlaylistEntryEndpoint
{
    public static void Map(IEndpointRouteBuilder playlists) => playlists
        .MapPost("/{id:guid}/tracks/move", Handle)
        .Validate<MovePlaylistEntryRequest>()
        .WithName("MovePlaylistEntry")
        .WithSummary("Reorder a playlist by moving one entry")
        .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<Results<NoContent, ProblemHttpResult>> Handle(
        Guid id, MovePlaylistEntryRequest request, ICommandHandler<MovePlaylistEntryCommand, MovePlaylistEntryResult> handler,
        CancellationToken cancellationToken) =>
        await handler.Handle(new MovePlaylistEntryCommand(new PlaylistId(id), request.From, request.To), cancellationToken) switch
        {
            Done => TypedResults.NoContent(),
            NotFoundError notFound => notFound.ToProblem(),
        };
}
