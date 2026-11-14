using FluentValidation;
using OldCountry.Domain.Playlists;

namespace OldCountry.Application.Playlists;

/// <summary>Create and update use it, because they take the same body.</summary>
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
