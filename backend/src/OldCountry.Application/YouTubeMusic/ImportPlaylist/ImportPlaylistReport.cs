namespace OldCountry.Application.YouTubeMusic.ImportPlaylist;

/// <summary>The result of an import.</summary>
/// <param name="PlaylistId">The library playlist.</param>
/// <param name="Name">The name of the playlist.</param>
/// <param name="IsNew">
/// <c>true</c> when this import made the playlist. <c>false</c> when the YouTube playlist was
/// imported before, and this import synced that library playlist.
/// </param>
/// <param name="Entries">The number of entries in the playlist now.</param>
/// <param name="Added">
/// The entries that this import added: all tracks for a new playlist. For a sync, only the tracks
/// added on YouTube since the last sync. Tracks that the user removed from the library playlist stay removed.
/// </param>
/// <param name="TracksCreated">The library tracks made for videos that the library did not have (not downloaded).</param>
/// <param name="TracksExisting">The videos that were already library tracks.</param>
/// <param name="Skipped">The videos that the import skipped: no duration, or after the size limit of the playlist.</param>
public sealed record ImportPlaylistReport(
    Guid PlaylistId, string Name, bool IsNew, int Entries, int Added, int TracksCreated, int TracksExisting, int Skipped);
