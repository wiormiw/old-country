using OldCountry.Api.Common.Library;
using OldCountry.Api.Common;
using OldCountry.Api.Common.Messaging;
using OldCountry.Api.Common.Validation;
using OldCountry.Api.Features.Tracks.AddTrack;
using OldCountry.Api.Features.Tracks.GetTrack;
using OldCountry.Api.Features.Tracks.ListTracks;
using OldCountry.Api.Features.Tracks.ScanLibrary;
using OldCountry.Api.Features.Tracks.StreamTrack;

namespace OldCountry.Api.Features.Tracks;

/// <summary>The library's tracks: everything this feature registers and exposes, listed by hand.</summary>
internal static class TracksFeature
{
    public static void AddServices(IServiceCollection services)
    {
        // Request validators (see Common/Validation), run by .Validate<T>() on the endpoints.
        services.AddValidator<AddTrackRequest, AddTrackRequestValidator>();
        services.AddValidator<ListTracksRequest, ListTracksRequestValidator>();

        // Use cases, each wrapped in its decorator pipeline (see HandlerRegistration).
        services.AddCommand<AddTrackCommand, AddTrackResult, AddTrackHandler>();
        services.AddQuery<GetTrackQuery, GetTrackResult, GetTrackHandler>();
        services.AddQuery<ListTracksQuery, PagedResponse<TrackResponse>, ListTracksHandler>();
        services.AddCommand<ScanLibraryCommand, ScanLibraryResult, ScanLibraryHandler>();
        services.AddQuery<StreamTrackQuery, StreamTrackResult, StreamTrackHandler>();

        // Library scanning: configuration, the background worker, and the state it shares with the API.
        services.AddOptions<LibraryOptions>()
            .BindConfiguration(LibraryOptions.Section)
            .ValidateWithValidator<LibraryOptions, LibraryOptionsValidator>()
            .ValidateOnStart();
        services.AddSingleton<IAudioMetadataReader, TagLibMetadataReader>();
        services.AddSingleton<LibraryScanQueue>();
        services.AddSingleton<LibraryScanStatus>();
        services.AddHostedService<LibraryScanner>();
    }

    public static void MapEndpoints(IEndpointRouteBuilder api)
    {
        var tracks = api.MapGroup("/tracks").WithTags("Tracks");
        AddTrackEndpoint.Map(tracks);
        GetTrackEndpoint.Map(tracks);
        ListTracksEndpoint.Map(tracks);
        ScanLibraryEndpoints.Map(tracks);
        StreamTrackEndpoint.Map(tracks);
    }
}
