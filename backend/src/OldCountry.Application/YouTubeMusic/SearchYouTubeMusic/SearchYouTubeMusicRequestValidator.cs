using FluentValidation;

namespace OldCountry.Application.YouTubeMusic.SearchYouTubeMusic;

internal sealed class SearchYouTubeMusicRequestValidator : AbstractValidator<SearchYouTubeMusicRequest>
{
    public const int MaxLimit = 50;

    public SearchYouTubeMusicRequestValidator()
    {
        RuleFor(r => r.Query)
            .NotEmpty().WithMessage("Type something to search for.")
            .MaximumLength(200).WithMessage("Keep the search to 200 characters.");
        RuleFor(r => r.Limit).InclusiveBetween(1, MaxLimit).WithMessage($"Ask for between 1 and {MaxLimit} results.");
    }
}
