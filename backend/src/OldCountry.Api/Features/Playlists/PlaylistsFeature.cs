using OldCountry.Api.Common.Messaging;
using OldCountry.Api.Common.Validation;
using OldCountry.Api.Features.Playlists.AddPlaylistTracks;
using OldCountry.Api.Features.Playlists.CreatePlaylist;
using OldCountry.Api.Features.Playlists.DeletePlaylist;
using OldCountry.Api.Features.Playlists.GetPlaylist;
using OldCountry.Api.Features.Playlists.ListPlaylists;
using OldCountry.Api.Features.Playlists.MovePlaylistEntry;
using OldCountry.Api.Features.Playlists.RemovePlaylistEntry;
using OldCountry.Api.Features.Playlists.UpdatePlaylist;

namespace OldCountry.Api.Features.Playlists;

/// <summary>Playlists: ordered, editable lists of library tracks.</summary>
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

    public static void MapEndpoints(IEndpointRouteBuilder api)
    {
        var playlists = api.MapGroup("/playlists").WithTags("Playlists");
        CreatePlaylistEndpoint.Map(playlists);
        ListPlaylistsEndpoint.Map(playlists);
        GetPlaylistEndpoint.Map(playlists);
        UpdatePlaylistEndpoint.Map(playlists);
        DeletePlaylistEndpoint.Map(playlists);
        AddPlaylistTracksEndpoint.Map(playlists);
        RemovePlaylistEntryEndpoint.Map(playlists);
        MovePlaylistEntryEndpoint.Map(playlists);
    }
}
