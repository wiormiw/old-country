using FluentValidation;
using OldCountry.Domain.YouTube;

namespace OldCountry.Application.Downloads.QueueDownloads;

internal sealed class DownloadTracksRequestValidator : AbstractValidator<DownloadTracksRequest>
{
    public const int MaxPerRequest = 50;

    public DownloadTracksRequestValidator()
    {
        RuleFor(r => r.VideoIds)
            .NotEmpty().WithMessage("Pick at least one video to download.")
            .Must(ids => ids.Count <= MaxPerRequest).WithMessage($"Download at most {MaxPerRequest} videos at a time.");
        RuleForEach(r => r.VideoIds)
            .Must(id => YouTubeVideoId.Create(id).Value is YouTubeVideoId)
            .WithMessage("'{PropertyValue}' is not a YouTube video id (11 characters of letters, digits, '-' or '_').");
    }
}
