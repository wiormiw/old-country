using OldCountry.Application.Abstractions.Messaging;

namespace OldCountry.Application.YouTubeMusic.SearchYouTubeMusic;

public sealed record SearchYouTubeMusicQuery(string Query, int Limit) : IQuery<SearchYouTubeMusicResult>;
