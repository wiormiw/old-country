using FluentValidation;
using Microsoft.Extensions.Options;
using OldCountry.Application.Common.YouTube;

namespace OldCountry.Application.YouTubeMusic.ImportPlaylist;

internal sealed class ImportPlaylistRequestValidator : AbstractValidator<ImportPlaylistRequest>
{
    public ImportPlaylistRequestValidator(IOptions<YouTubeMusicOptions> options)
    {
        RuleFor(r => r.Playlist)
            .NotEmpty().WithMessage("Say which playlist to import: \"liked\", a playlist URL, or a playlist id.")
            .Must(p => PlaylistSource.ToUrl(p) is not null)
            .WithMessage("That isn't a YouTube Music playlist. Use \"liked\", a URL with ?list=..., or the list id itself.");
        RuleFor(r => r.Playlist)
            .Must(_ => options.Value.CookiesFile is not null)
            .When(r => PlaylistSource.IsLiked(r.Playlist))
            .WithMessage("Importing liked songs needs your YouTube Music cookies: set YouTubeMusic:CookiesFile to an exported cookies.txt.");
    }
}
