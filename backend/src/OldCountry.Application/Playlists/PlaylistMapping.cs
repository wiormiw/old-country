using OldCountry.Domain.Playlists;

namespace OldCountry.Application.Playlists;

internal static class PlaylistMapping
{
    public static PlaylistSummaryResponse ToSummary(this Playlist playlist) => new(
        playlist.Id.Value, playlist.Name, playlist.Description, playlist.Entries.Count, playlist.CreatedAt, playlist.UpdatedAt,
        playlist.YouTubeListId, playlist.YouTubeSyncedAt);
}
