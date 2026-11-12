using FluentValidation;
using OldCountry.Domain.Library;

namespace OldCountry.Api.Features.Playlists;

/// <summary>A playlist's name and description, for creating or editing it.</summary>
public sealed record PlaylistDetailsRequest
{
    /// <summary>Playlist name, 1-200 characters.</summary>
    public required string Name { get; init; }

    /// <summary>Optional free-text description, up to 2000 characters.</summary>
    public string? Description { get; init; }
}

/// <summary>Shared by create and update, which take the same body.</summary>
internal sealed class PlaylistDetailsRequestValidator : AbstractValidator<PlaylistDetailsRequest>
{
    public PlaylistDetailsRequestValidator()
    {
        RuleFor(r => r.Name)
            .NotEmpty().WithMessage("Give the playlist a name.")
            .MaximumLength(Playlist.NameMaxLength).WithMessage($"Keep the name to {Playlist.NameMaxLength} characters.");
        RuleFor(r => r.Description)
            .MaximumLength(Playlist.DescriptionMaxLength).WithMessage($"Keep the description to {Playlist.DescriptionMaxLength} characters.");
    }
}

/// <summary>A playlist without its tracks, for lists.</summary>
/// <param name="Id">The playlist's id.</param>
/// <param name="Name">Playlist name.</param>
/// <param name="Description">Optional description.</param>
/// <param name="TrackCount">Number of entries, duplicates included.</param>
/// <param name="CreatedAt">When the playlist was created.</param>
/// <param name="UpdatedAt">When it last changed, if ever.</param>
public sealed record PlaylistSummaryResponse(
    Guid Id,
    string Name,
    string? Description,
    int TrackCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>One slot of a playlist, with the track it plays.</summary>
/// <param name="Position">Zero-based index; use it to remove or move this entry.</param>
/// <param name="TrackId">The track's id.</param>
/// <param name="Title">Track title.</param>
/// <param name="Artist">Artist name.</param>
/// <param name="Duration">Track length.</param>
/// <param name="IsAvailableOffline">Whether the track has a local file.</param>
/// <param name="AddedAt">When the track was added to this playlist.</param>
public sealed record PlaylistEntryResponse(
    int Position,
    Guid TrackId,
    string Title,
    string Artist,
    TimeSpan Duration,
    bool IsAvailableOffline,
    DateTimeOffset AddedAt);

/// <summary>A playlist with its tracks in order.</summary>
/// <param name="Id">The playlist's id.</param>
/// <param name="Name">Playlist name.</param>
/// <param name="Description">Optional description.</param>
/// <param name="Entries">The tracks, in play order.</param>
/// <param name="TotalDuration">Sum of the entries' durations.</param>
/// <param name="CreatedAt">When the playlist was created.</param>
/// <param name="UpdatedAt">When it last changed, if ever.</param>
public sealed record PlaylistResponse(
    Guid Id,
    string Name,
    string? Description,
    IReadOnlyList<PlaylistEntryResponse> Entries,
    TimeSpan TotalDuration,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

internal static class PlaylistMapping
{
    public static PlaylistSummaryResponse ToSummary(this Playlist playlist) => new(
        playlist.Id.Value, playlist.Name, playlist.Description, playlist.Entries.Count, playlist.CreatedAt, playlist.UpdatedAt);
}
