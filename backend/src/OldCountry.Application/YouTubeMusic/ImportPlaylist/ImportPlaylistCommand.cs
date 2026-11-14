using OldCountry.Application.Abstractions.Messaging;

namespace OldCountry.Application.YouTubeMusic.ImportPlaylist;

/// <summary>Imports a YouTube Music playlist. It holds no transaction while yt-dlp reads the playlist. The writes occur in one save.</summary>
[NoTransaction]
public sealed record ImportPlaylistCommand(Uri PlaylistUrl) : ICommand<ImportPlaylistResult>;
