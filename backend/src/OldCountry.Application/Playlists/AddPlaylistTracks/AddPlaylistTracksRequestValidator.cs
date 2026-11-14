using FluentValidation;

namespace OldCountry.Application.Playlists.AddPlaylistTracks;

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
