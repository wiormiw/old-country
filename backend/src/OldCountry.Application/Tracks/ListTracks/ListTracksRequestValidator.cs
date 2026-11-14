using FluentValidation;

namespace OldCountry.Application.Tracks.ListTracks;

internal sealed class ListTracksRequestValidator : AbstractValidator<ListTracksRequest>
{
    public const int MaxPageSize = 200;

    public ListTracksRequestValidator()
    {
        RuleFor(r => r.Page).GreaterThanOrEqualTo(1).WithMessage("Pages start at 1.");
        RuleFor(r => r.PageSize)
            .InclusiveBetween(1, MaxPageSize).WithMessage($"Ask for between 1 and {MaxPageSize} tracks per page.");
        RuleFor(r => r.Search).MaximumLength(200).WithMessage("Keep the search to 200 characters.");
    }
}
