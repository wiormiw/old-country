using OldCountry.Domain.Errors;

namespace OldCountry.Application.YouTubeMusic.ImportPlaylist;

public readonly union ImportPlaylistResult(ImportPlaylistReport, ExternalServiceError);
