using FluentValidation;
using Microsoft.EntityFrameworkCore;
using OldCountry.Domain.Tracks;
using OldCountry.Domain.YouTube;

namespace OldCountry.Application.Tracks.AddTrack;

internal sealed class AddTrackRequestValidator : AbstractValidator<AddTrackRequest>
{
    public AddTrackRequestValidator()
    {
        RuleFor(r => r.Title)
            .NotEmpty().WithMessage("Give the track a title.")
            .MaximumLength(Track.TitleMaxLength);

        RuleFor(r => r.Artist)
            .NotEmpty().WithMessage("Give the artist's name; use \"Unknown Artist\" if you don't know it.")
            .MaximumLength(Domain.Artists.Artist.NameMaxLength);

        RuleFor(r => r.DurationSeconds)
            .InclusiveBetween(1, 24 * 60 * 60).WithMessage("Duration must be between 1 second and 24 hours.");

        // At least one source. The error uses the key "source", the same as the rule of the domain.
        RuleFor(r => r)
            .Must(r => r.LocalPath is not null || r.YouTubeVideoId is not null)
            .WithMessage("A track needs a local file path, a YouTube video id, or both.")
            .OverridePropertyName("source");

        When(r => r.LocalPath is not null, () =>
        {
            RuleFor(r => r.LocalPath!)
                .MaximumLength(4096)
                .Must(Path.IsPathFullyQualified).WithMessage("The local path must be absolute, e.g. /srv/music/song.flac.");
            RuleFor(r => r.SizeBytes)
                .NotNull().WithMessage("Give the file size in bytes along with the local path.")
                .GreaterThan(0);
            RuleFor(r => r.BitrateKbps).InclusiveBetween(0, 10_000);
            RuleFor(r => r.SampleRateHz).InclusiveBetween(0, 768_000);
        });

        // Use the rule of the domain for a valid id. Do not write it again here.
        RuleFor(r => r.YouTubeVideoId)
            .Must(id => Domain.YouTube.YouTubeVideoId.Create(id).Value is YouTubeVideoId)
            .When(r => r.YouTubeVideoId is not null)
            .WithMessage("A YouTube video id is the 11 characters after \"v=\" in the video's URL.");
    }
}
