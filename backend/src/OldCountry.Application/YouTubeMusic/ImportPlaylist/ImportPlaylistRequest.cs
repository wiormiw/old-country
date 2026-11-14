namespace OldCountry.Application.YouTubeMusic.ImportPlaylist;

/// <summary>A YouTube Music playlist to import, or to sync when it was imported before.</summary>
public sealed record ImportPlaylistRequest
{
    /// <summary><c>liked</c> for your liked songs, a playlist URL, or a playlist id.</summary>
    public required string Playlist { get; init; }
}
