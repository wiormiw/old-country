using FluentValidation;

namespace OldCountry.Application.Playlists.MovePlaylistEntry;

internal sealed class MovePlaylistEntryRequestValidator : AbstractValidator<MovePlaylistEntryRequest>
{
    public MovePlaylistEntryRequestValidator()
    {
        RuleFor(r => r.From).GreaterThanOrEqualTo(0).WithMessage("Positions start at 0.");
        RuleFor(r => r.To).GreaterThanOrEqualTo(0).WithMessage("Positions start at 0.");
        RuleFor(r => r.To).NotEqual(r => r.From).WithMessage("The entry is already at that position.");
    }
}
