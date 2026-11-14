using OldCountry.Application.Downloads.DownloadTrack;
using OldCountry.Application.Downloads.ListDownloads;
using OldCountry.Application.Downloads.ProcessNextDownload;
using OldCountry.Application.Downloads.QueueDownloads;
using OldCountry.Application.Downloads.QueuePlaylistDownloads;
using OldCountry.Application.Pipeline;
using OldCountry.Application.Pipeline.Validation;
namespace OldCountry.Application.Downloads;

/// <summary>
/// Downloads of YouTube audio into the library. The jobs are rows in PostgreSQL.
/// One background worker takes them one at a time, under a lease.
/// </summary>
internal static class DownloadsFeature
{
    public static void AddServices(IServiceCollection services)
    {
        // The doorbell wakes the worker when jobs are queued.
        services.AddSingleton<DownloadDoorbell>();

        services.AddValidator<DownloadTracksRequest, DownloadTracksRequestValidator>();

        services.AddCommand<DownloadTrackCommand, DownloadTrackResult, DownloadTrackHandler>();
        services.AddCommand<QueueDownloadsCommand, QueueDownloadsResult, QueueDownloadsHandler>();
        services.AddCommand<QueuePlaylistDownloadsCommand, QueuePlaylistDownloadsResult, QueuePlaylistDownloadsHandler>();
        services.AddQuery<ListDownloadsQuery, DownloadsResponse, ListDownloadsHandler>();
        services.AddCommand<ProcessNextDownloadCommand, ProcessNextDownloadResult, ProcessNextDownloadHandler>();
    }
}
