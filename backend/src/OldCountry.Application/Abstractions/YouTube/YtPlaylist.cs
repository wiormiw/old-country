namespace OldCountry.Application.Abstractions.YouTube;

public sealed record YtPlaylist(string Title, IReadOnlyList<YtVideo> Entries);
