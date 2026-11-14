using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using OldCountry.Api.Http;
using OldCountry.Application.Common.Library;
using OldCountry.Application.Tracks.ScanLibrary;

namespace OldCountry.Api.Endpoints.Tracks;

internal static class ScanLibraryEndpoints
{
    public const string StatusName = "GetLibraryScanStatus";

    public static void Map(IEndpointRouteBuilder tracks)
    {
        tracks.MapPost("/scans", RequestScan)
            .WithName("ScanLibrary")
            .WithSummary("Scan the library folders for new audio files")
            .WithDescription("Queues a scan and returns at once. A scan already waiting absorbs this request. Poll the status endpoint for the result.");

        tracks.MapGet("/scans/latest", GetStatus)
            .WithName(StatusName)
            .WithSummary("Library scan status and the last scan's report");
    }

    private static Results<AcceptedAtRoute<ScanStatusResponse>, ValidationProblem> RequestScan(
        LibraryScanQueue queue, LibraryScanStatus status, IOptions<LibraryOptions> options)
    {
        if (LibraryOptions.Check(options.Value.Roots) is { } invalid) return invalid.ToProblem();

        queue.Request(ScanTrigger.Manual);
        return TypedResults.AcceptedAtRoute(status.Snapshot(queue), StatusName);
    }

    private static Ok<ScanStatusResponse> GetStatus(LibraryScanQueue queue, LibraryScanStatus status) =>
        TypedResults.Ok(status.Snapshot(queue));
}
