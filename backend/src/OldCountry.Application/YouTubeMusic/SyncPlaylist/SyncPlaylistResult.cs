using OldCountry.Application.YouTubeMusic.ImportPlaylist;
using OldCountry.Domain.Errors;

namespace OldCountry.Application.YouTubeMusic.SyncPlaylist;

public readonly union SyncPlaylistResult(ImportPlaylistReport, NotFoundError, ValidationError, ExternalServiceError);
