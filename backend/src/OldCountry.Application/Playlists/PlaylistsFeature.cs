using OldCountry.Application.Pipeline;
using OldCountry.Application.Pipeline.Validation;
using OldCountry.Application.Playlists.AddPlaylistTracks;
using OldCountry.Application.Playlists.CreatePlaylist;
using OldCountry.Application.Playlists.DeletePlaylist;
using OldCountry.Application.Playlists.GetPlaylist;
using OldCountry.Application.Playlists.ListPlaylists;
using OldCountry.Application.Playlists.MovePlaylistEntry;
using OldCountry.Application.Playlists.RemovePlaylistEntry;
using OldCountry.Application.Playlists.UpdatePlaylist;
namespace OldCountry.Application.Playlists;

/// <summary>Playlists: ordered lists of library tracks that the user can change.</summary>
internal static class PlaylistsFeature
{
    public static void AddServices(IServiceCollection services)
    {
        services.AddValidator<PlaylistDetailsRequest, PlaylistDetailsRequestValidator>();
        services.AddValidator<AddPlaylistTracksRequest, AddPlaylistTracksRequestValidator>();
        services.AddValidator<MovePlaylistEntryRequest, MovePlaylistEntryRequestValidator>();

        services.AddCommand<CreatePlaylistCommand, CreatePlaylistResult, CreatePlaylistHandler>();
        services.AddQuery<ListPlaylistsQuery, IReadOnlyList<PlaylistSummaryResponse>, ListPlaylistsHandler>();
        services.AddQuery<GetPlaylistQuery, GetPlaylistResult, GetPlaylistHandler>();
        services.AddCommand<UpdatePlaylistCommand, UpdatePlaylistResult, UpdatePlaylistHandler>();
        services.AddCommand<DeletePlaylistCommand, DeletePlaylistResult, DeletePlaylistHandler>();
        services.AddCommand<AddPlaylistTracksCommand, AddPlaylistTracksResult, AddPlaylistTracksHandler>();
        services.AddCommand<RemovePlaylistEntryCommand, RemovePlaylistEntryResult, RemovePlaylistEntryHandler>();
        services.AddCommand<MovePlaylistEntryCommand, MovePlaylistEntryResult, MovePlaylistEntryHandler>();
    }
}
