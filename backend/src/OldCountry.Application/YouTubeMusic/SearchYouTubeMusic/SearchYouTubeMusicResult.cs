using OldCountry.Domain.Errors;

namespace OldCountry.Application.YouTubeMusic.SearchYouTubeMusic;

public readonly union SearchYouTubeMusicResult(IReadOnlyList<YouTubeSearchResult>, ExternalServiceError);
