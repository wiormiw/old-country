using OldCountry.Application.Common.Library;
using OldCountry.Application.Common.Paging;
using OldCountry.Application.Pipeline;
using OldCountry.Application.Pipeline.Validation;
using OldCountry.Application.Tracks.AddTrack;
using OldCountry.Application.Tracks.GetTrack;
using OldCountry.Application.Tracks.ListTracks;
using OldCountry.Application.Tracks.ScanLibrary;
using OldCountry.Application.Tracks.StreamTrack;
namespace OldCountry.Application.Tracks;

/// <summary>The tracks of the library: the services of this feature, in a list that you write by hand.</summary>
internal static class TracksFeature
{
    public static void AddServices(IServiceCollection services)
    {
        // The request validators. The endpoints run them before the handlers.
        services.AddValidator<AddTrackRequest, AddTrackRequestValidator>();
        services.AddValidator<ListTracksRequest, ListTracksRequestValidator>();

        // The use cases. Each one has its decorator pipeline (see HandlerRegistration).
        services.AddCommand<AddTrackCommand, AddTrackResult, AddTrackHandler>();
        services.AddQuery<GetTrackQuery, GetTrackResult, GetTrackHandler>();
        services.AddQuery<ListTracksQuery, PagedResponse<TrackResponse>, ListTracksHandler>();
        services.AddCommand<ScanLibraryCommand, ScanLibraryResult, ScanLibraryHandler>();
        services.AddQuery<StreamTrackQuery, StreamTrackResult, StreamTrackHandler>();

        // The library scan: its settings, and the state that the scan loop and the API share.
        services.AddOptions<LibraryOptions>()
            .BindConfiguration(LibraryOptions.Section)
            .ValidateWithValidator<LibraryOptions, LibraryOptionsValidator>()
            .ValidateOnStart();
        services.AddSingleton<LibraryScanQueue>();
        services.AddSingleton<LibraryScanStatus>();
    }
}
