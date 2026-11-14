using FluentValidation;

namespace OldCountry.Application.Common.Library;

/// <summary>
/// Runs one time at startup. Thus a typo in the settings stops the server with a clear message, before the first scan.
/// The scan itself checks that each folder exists, because a disk can be mounted after the server starts.
/// </summary>
internal sealed class LibraryOptionsValidator : AbstractValidator<LibraryOptions>
{
    public LibraryOptionsValidator()
    {
        RuleForEach(o => o.Roots)
            .Must(Path.IsPathFullyQualified).WithMessage("Library folder '{PropertyValue}' must be an absolute path.");
        RuleFor(o => o.RescanInterval)
            .InclusiveBetween(TimeSpan.Zero, TimeSpan.FromDays(7))
            .WithMessage("RescanInterval must be between 00:00:00 (off) and 7.00:00:00 (a week).");
    }
}
